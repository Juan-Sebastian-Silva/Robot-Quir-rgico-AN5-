using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FulcroCommandSender : MonoBehaviour
{
    [Header("Referencias obligatorias")]
    [Tooltip("El script IK_calc que está en el GameObject AN5")]
    public IK_calc ikCalc;

    [Tooltip("El Ros2CommandSender que ya existe en la escena")]
    public Ros2CommandSender ros2CommandSender;

    [Header("Sincronización al activar Fulcro")]
    [Tooltip("Suscriptor para leer posición actual del robot real")]
    public JointPositionSubscriber jointPositionSubscriber;
    [Tooltip("IKController del panel de Fulcro")]
    public IKController ikController;

    [Header("Pose Home del Fulcro")]
    [Tooltip("Activar para mover el robot a la pose home al activar el fulcro")]
    public bool usarPoseHome = true;
    [Tooltip("Ángulos en grados de la pose home del fulcro (J1..J6)")]
    public float homePoseJ1 =   0f;
    public float homePoseJ2 = -90f;
    public float homePoseJ3 =  90f;
    public float homePoseJ4 = -90f;
    public float homePoseJ5 = -90f;
    public float homePoseJ6 =   0f;
    [Tooltip("Velocidad para ir a la pose home (1-30)")]
    [Range(1, 30)]
    public float velocidadHome = 10f;

    [Header("UI")]
    [Tooltip("Botón 'Enviar Fulcro' en el panel")]
    public Button sendButton;

    [Tooltip("Toggle para activar/desactivar el modo Fulcro desde el panel")]
    public Toggle modeToggle;

    [Tooltip("Texto del toggle para mostrar estado actual")]
    public Text modeLabel;

    [Header("Parámetros de movimiento")]
    [Tooltip("Velocidad de movimiento al robot real (1-100)")]
    [Range(1, 100)]
    public float speed = 10f;

    [Tooltip("Slider de velocidad opcional — si se asigna, sobreescribe el campo speed")]
    public Slider speedSlider;

    private bool _fulcroModeActive = false;

    // ------------------------------------------------------------------ //
    //  Inicialización
    // ------------------------------------------------------------------ //
    void Start()
    {
        ValidateReferences();

        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendButtonPressed);
            sendButton.interactable = false;
        }

        if (modeToggle != null)
        {
            modeToggle.isOn = false;
            modeToggle.onValueChanged.AddListener(OnModeToggleChanged);
        }

        if (speedSlider != null)
        {
            speedSlider.minValue = 1f;
            speedSlider.maxValue = 100f;
            speedSlider.value    = speed;
            speedSlider.onValueChanged.AddListener(v => speed = v);
        }

        UpdateModeLabel();
    }

    // ------------------------------------------------------------------ //
    //  API pública para FulcroPanel
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Llamado por FulcroPanel cuando el usuario presiona el botón Enviar.
    /// </summary>
    public void SendFromPanel()
    {
        if (!_fulcroModeActive)
        {
            Debug.LogWarning("[FulcroCommandSender] Modo Fulcro no activo. Activa el toggle primero.");
            return;
        }

        if (ikCalc == null || IK_calc.goodSolution == null || IK_calc.goodSolution.Count < 6)
        {
            Debug.LogError("[FulcroCommandSender] No hay solución IK disponible.");
            return;
        }

        StartCoroutine(SendFulcroCommand());
    }

    /// <summary>
    /// Llamado por FulcroPanel cuando cambia el Toggle de modo.
    /// Activa/desactiva IK_calc y el estado interno sin pasar por el Toggle UI.
    /// </summary>
    public void SetModeActive(bool isOn)
    {
        _fulcroModeActive = isOn;

        if (isOn)
        {
            StartCoroutine(ActivarFulcroSincronizado());
        }
        else
        {
            if (sendButton != null) sendButton.interactable = false;
            if (jointPositionSubscriber != null) jointPositionSubscriber.StartUpdating();
            UpdateModeLabel();
        }

        Debug.Log($"[FulcroCommandSender] SetModeActive: {(isOn ? "ACTIVO" : "INACTIVO")}");
    }

    /// <summary>
    /// Activa el modo Fulcro de forma sincronizada:
    /// 1. Mantiene IK_calc deshabilitado
    /// 2. Lee posición actual del robot real y la aplica al virtual
    /// 3. Espera un frame para que los joints se asienten
    /// 4. Habilita IK_calc — en ese primer Update usa los joints ya correctos
    /// </summary>
    private IEnumerator ActivarFulcroSincronizado()
    {
        // Paso 1: deshabilitar IK_calc
        if (ikCalc != null) ikCalc.enabled = false;

        float[] homeAngles = { homePoseJ1, homePoseJ2, homePoseJ3,
                               homePoseJ4, homePoseJ5, homePoseJ6 };

        if (usarPoseHome)
        {
            // Paso 2A: preparar robot y mover a pose home
            if (ros2CommandSender != null)
            {
                ros2CommandSender.SendCommand("DragTeachSwitch(0)");
                yield return new WaitForSeconds(0.05f);
                ros2CommandSender.SendCommand("StopMotion()");
                yield return new WaitForSeconds(0.05f);
                ros2CommandSender.SendCommand("ResetAllError()");
                yield return new WaitForSeconds(0.05f);
                ros2CommandSender.SendCommand("StartJOG(0,6,0,100)");
                yield return new WaitForSeconds(0.5f);
                ros2CommandSender.SendCommand("StartJOG(0,6,1,100)");
                yield return new WaitForSeconds(0.5f);

                ros2CommandSender.SendCommand(
                    $"JNTPoint(1,{homePoseJ1:F4},{homePoseJ2:F4},{homePoseJ3:F4}," +
                    $"{homePoseJ4:F4},{homePoseJ5:F4},{homePoseJ6:F4})");
                yield return new WaitForSeconds(0.05f);
                ros2CommandSender.SendCommand($"MoveJ(JNT1,{velocidadHome:F0})");
                Debug.Log("[FulcroCommandSender] Moviendo robot real a pose home.");
            }

            // Aplicar pose home al robot virtual
            if (ikCalc != null && ikCalc.robot != null && ikCalc.robot.Count == 6)
                for (int i = 0; i < 6; i++)
                    ikCalc.robot[i].localEulerAngles = ConvertJointAnglesForSync(homeAngles[i], i);

            // Esperar que el robot real llegue
            if (jointPositionSubscriber != null)
            {
                float timeout = 30f, elapsed = 0f;
                bool  reached = false;
                while (!reached && elapsed < timeout)
                {
                    float[] current = jointPositionSubscriber.GetLastKnownPositions();
                    if (current != null && current.Length >= 6)
                    {
                        reached = true;
                        for (int i = 0; i < 6; i++)
                            if (Mathf.Abs(current[i] - homeAngles[i]) > 1f) { reached = false; break; }
                    }
                    yield return new WaitForSeconds(0.1f);
                    elapsed += 0.1f;
                }
                Debug.Log(reached ? "[FulcroCommandSender] Pose home alcanzada."
                                  : "[FulcroCommandSender] Timeout — continuando.");
            }
            else
                yield return new WaitForSeconds(3f);
        }
        else
        {
            // Paso 2B: sincronizar con posición actual del robot real
            if (jointPositionSubscriber != null)
            {
                float[] posReal      = jointPositionSubscriber.GetLastKnownPositions();
                bool    tieneValores = false;
                if (posReal != null && posReal.Length >= 6)
                    foreach (float v in posReal)
                        if (Mathf.Abs(v) > 0.01f) { tieneValores = true; break; }

                if (tieneValores && ikCalc != null && ikCalc.robot != null && ikCalc.robot.Count == 6)
                    for (int i = 0; i < 6; i++)
                        ikCalc.robot[i].localEulerAngles = ConvertJointAnglesForSync(posReal[i], i);
            }
        }

        // Paso 3: esperar dos frames
        yield return null;
        yield return null;

        // Paso 4: actualizar referencia del IKController — sliders en 0
        if (ikController != null)
        {
            ikController.ActualizarReferencia();
            Debug.Log("[FulcroCommandSender] Referencia IKController actualizada.");
        }

        // Paso 5: habilitar IK_calc
        if (ikCalc != null) ikCalc.enabled = true;
        if (sendButton != null) sendButton.interactable = true;
        UpdateModeLabel();
        Debug.Log("[FulcroCommandSender] Fulcro activo.");
    }

    // ------------------------------------------------------------------ //
    //  Seguimiento continuo: robot real → robot virtual
    //  Mientras el fulcro está activo, lee la posición del robot real
    //  y la aplica al virtual via JointStateWriters.
    /// <summary>
    /// Convierte ángulo del robot real (grados) al Vector3 de Euler
    /// del modelo Unity — misma lógica que IK_calc.ConvertJointAngles
    /// pero recibiendo grados directamente desde el robot real.
    /// </summary>
    private Vector3 ConvertJointAnglesForSync(float angleDeg, int jointIndex)
    {
        switch (jointIndex)
        {
            case 0: return new Vector3(0,      -angleDeg + 27f,  0);
            case 1: return new Vector3(-angleDeg + 18f, 0,      -90);
            case 2: return new Vector3(0,      -angleDeg,        0);
            case 3: return new Vector3(0,      -angleDeg - 10f,  0);
            case 4: return new Vector3(-angleDeg - 27f, 0,      -90);
            case 5: return new Vector3( angleDeg,        0,      90);
            default: return Vector3.zero;
        }
    }

    // ------------------------------------------------------------------ //
    //  Toggle de modo Fulcro (desde UI directa)
    // ------------------------------------------------------------------ //
    private void OnModeToggleChanged(bool isOn)
    {
        SetModeActive(isOn);
    }

    // ------------------------------------------------------------------ //
    //  Rotación panorámica J6
    // ------------------------------------------------------------------ //
    //  Envío de ángulos al robot real
    // ------------------------------------------------------------------ //
    private void OnSendButtonPressed()
    {
        if (!_fulcroModeActive)
        {
            Debug.LogWarning("[FulcroCommandSender] Modo Fulcro no está activo.");
            return;
        }

        if (ikCalc == null || IK_calc.goodSolution == null || IK_calc.goodSolution.Count < 6)
        {
            Debug.LogError("[FulcroCommandSender] No hay solución IK disponible.");
            return;
        }

        StartCoroutine(SendFulcroCommand());
    }

    private IEnumerator SendFulcroCommand()
    {
        float currentSpeed = (speedSlider != null) ? speedSlider.value : speed;

        float j1 = (float)(Mathf.Rad2Deg * IK_calc.goodSolution[0]);
        float j2 = (float)(Mathf.Rad2Deg * IK_calc.goodSolution[1]);
        float j3 = (float)(Mathf.Rad2Deg * IK_calc.goodSolution[2]);
        float j4 = (float)(Mathf.Rad2Deg * IK_calc.goodSolution[3]);
        float j5 = (float)(Mathf.Rad2Deg * IK_calc.goodSolution[4]);
        float j6 = (float)(Mathf.Rad2Deg * IK_calc.goodSolution[5]);

        Debug.Log($"[FulcroCommandSender] J1={j1:F2} J2={j2:F2} J3={j3:F2} J4={j4:F2} J5={j5:F2} J6={j6:F2} Speed={currentSpeed}");

        ros2CommandSender.SendCommand("DragTeachSwitch(0)"); yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand("StopMotion()");       yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand("ResetAllError()");    yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand("StartJOG(0,6,0,100)"); yield return new WaitForSeconds(0.5f);
        ros2CommandSender.SendCommand("StartJOG(0,6,1,100)"); yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(0.5f);

        string jntCommand  = $"JNTPoint(1,{j1:F4},{j2:F4},{j3:F4},{j4:F4},{j5:F4},{j6:F4})";
        string moveCommand = $"MoveJ(JNT1,{currentSpeed:F0})";

        ros2CommandSender.SendCommand(jntCommand);  yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand(moveCommand);

        Debug.Log($"[FulcroCommandSender] Enviado: {jntCommand}");
        Debug.Log($"[FulcroCommandSender] Enviado: {moveCommand}");
    }

    // ------------------------------------------------------------------ //
    private void UpdateModeLabel()
    {
        if (modeLabel != null)
            modeLabel.text = _fulcroModeActive ? "FULCRO ACTIVO" : "FULCRO";
    }

    private void ValidateReferences()
    {
        if (ikCalc            == null) Debug.LogError("[FulcroCommandSender] ikCalc no asignado.");
        if (ros2CommandSender == null) Debug.LogError("[FulcroCommandSender] ros2CommandSender no asignado.");
        if (sendButton        == null) Debug.LogWarning("[FulcroCommandSender] sendButton no asignado.");
        if (modeToggle        == null) Debug.LogWarning("[FulcroCommandSender] modeToggle no asignado.");
    }
}