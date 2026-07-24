using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FulcroSequenceManager : MonoBehaviour
{
    [Header("Referencias")]
    public IK_calc             ikCalc;
    public Ros2CommandSender   ros2CommandSender;
    public IKController        ikController;
    public FulcroCommandSender fulcroCommandSender;
    public JointPositionSubscriber jointPositionSubscriber;

    [Header("UI — Botones")]
    public Button btnGuardarPunto;
    public Button btnEliminar;
    public Button btnEjecutar;
    public Button btnReset;
    public Button btnCargarTxt;
    public Button btnGuardarTxt;
    public Button btnSeleccionarTxt;
    public Button btnSincronizar;

    [Header("Parámetros")]
    [Range(1, 100)]
    public float defaultSpeed          = 10f;
    public float simulacionPausa       = 1.5f;
    [Range(1, 30)]
    public float velocidadSincronizacion = 10f;

    [Header("UI — Modo")]
    public Toggle   toggleSimular;
    public TMP_Text labelModo;
    [Tooltip("Activa el modo paso a paso: el robot espera en cada punto hasta presionar AVANZAR")]
    public Toggle   toggleModoEspera;
    [Tooltip("Botón para avanzar al siguiente punto en modo espera")]
    public Button   btnAvanzar;

    [Header("UI — Display")]
    [Tooltip("Asignar TMP_Text o Text legacy para mostrar la lista de puntos")]
    public TMP_Text    listaDisplay;
    public Text        listaDisplayLegacy; // alternativa si no usas TMP

    [Header("Pose Home del Fulcro")]
    [Tooltip("Ángulos en grados de la pose home — robot real en posición de inicio")]
    public float homePoseJ1 =  107.67f;
    public float homePoseJ2 = -109.87f;
    public float homePoseJ3 =  -61.02f;
    public float homePoseJ4 =  -92.98f;
    public float homePoseJ5 =   89.99f;
    public float homePoseJ6 = -133.92f;

    [Header("Archivo .txt")]
    public string savePath = "/home/an5/Downloads/Interfaces-AN5-Endo/MoveEndo";
    public string fileName  = "fulcro_sequence.txt";

    // ------------------------------------------------------------------ //
    private bool _modoSimulacion = false;
    private bool _modoEspera    = false;  // modo paso a paso
    private bool _avanzar       = false;  // señal para avanzar al siguiente punto
    private List<FulcroPoint> _puntos = new List<FulcroPoint>();

    // Pose del fulcro al momento de sincronizar — es el punto de inicio
    // al que el robot siempre regresa antes de ejecutar cada secuencia
    private float[] _poseFulcroInicial = null;  // ángulos en grados del robot real
    private bool    _fulcroInicialDefinido = false;

    private struct FulcroPoint
    {
        public float[] anglesRad;   // 6 ángulos en radianes
        public Vector3 ikPosition;  // posición mundial del IK al guardar
        public float   speed;
    }

    // ------------------------------------------------------------------ //
    //  Start
    // ------------------------------------------------------------------ //
    void Start()
    {
        if (btnGuardarPunto   != null) btnGuardarPunto.onClick.AddListener(GuardarPunto);
        if (btnEliminar       != null) btnEliminar.onClick.AddListener(EliminarUltimo);
        if (btnEjecutar       != null) btnEjecutar.onClick.AddListener(EjecutarSecuencia);
        if (btnReset          != null) btnReset.onClick.AddListener(ResetIK);
        if (btnCargarTxt      != null) btnCargarTxt.onClick.AddListener(CargarDesdeTxt);
        if (btnGuardarTxt     != null) btnGuardarTxt.onClick.AddListener(GuardarEnTxt);
        if (btnSeleccionarTxt != null) btnSeleccionarTxt.onClick.AddListener(AbrirExplorador);
        if (btnSincronizar    != null) btnSincronizar.onClick.AddListener(SincronizarRobotReal);

        if (toggleSimular != null)
        {
            toggleSimular.isOn = false;
            toggleSimular.onValueChanged.AddListener(OnToggleSimular);
        }

        if (toggleModoEspera != null)
        {
            toggleModoEspera.isOn = false;
            toggleModoEspera.onValueChanged.AddListener(isOn => _modoEspera = isOn);
        }

        if (btnAvanzar != null)
        {
            btnAvanzar.onClick.AddListener(() => _avanzar = true);
            btnAvanzar.interactable = false; // solo activo durante ejecución en modo espera
        }

        UpdateButtonStates();
        ActualizarDisplay();
        ActualizarLabelModo();
    }

    // ------------------------------------------------------------------ //
    //  Toggle
    // ------------------------------------------------------------------ //
    private void OnToggleSimular(bool isOn)
    {
        _modoSimulacion = isOn;
        ActualizarLabelModo();
    }

    private void ActualizarLabelModo()
    {
        if (labelModo != null)
            labelModo.text = _modoSimulacion ? "SIMULAR" : "ROBOT REAL";
    }

    // ------------------------------------------------------------------ //
    //  Guardar punto — posición mundial del IK
    // ------------------------------------------------------------------ //
    private void GuardarPunto()
    {
        if (ikCalc == null || IK_calc.goodSolution == null || IK_calc.goodSolution.Count < 6)
        {
            Debug.LogError("[FulcroSequenceManager] No hay solución IK disponible.");
            return;
        }

        float currentSpeed = defaultSpeed;

        FulcroPoint punto = new FulcroPoint
        {
            anglesRad  = new float[6],
            ikPosition = (ikCalc.ik != null) ? ikCalc.ik.position : Vector3.zero,
            speed      = currentSpeed
        };

        for (int i = 0; i < 6; i++)
            punto.anglesRad[i] = (float)IK_calc.goodSolution[i];

        _puntos.Add(punto);
        Debug.Log($"[FulcroSequenceManager] P{_puntos.Count} guardado. J1={Mathf.Rad2Deg * punto.anglesRad[0]:F2}°");

        UpdateButtonStates();
        ActualizarDisplay();
    }

    // ------------------------------------------------------------------ //
    //  Eliminar / Reset / Enviar
    // ------------------------------------------------------------------ //
    private void EliminarUltimo()
    {
        if (_puntos.Count == 0) return;
        _puntos.RemoveAt(_puntos.Count - 1);
        UpdateButtonStates();
        ActualizarDisplay();
    }

    private void ResetIK()
    {
        if (ikController != null) ikController.ResetPosition();
    }

    // ------------------------------------------------------------------ //
    //  SINCRONIZAR — envía la pose actual del robot virtual al robot real
    //  El usuario pulsa este botón antes de grabar puntos para que ambos
    //  robots partan del mismo estado y no haya desfase al ejecutar.
    // ------------------------------------------------------------------ //
    public void SincronizarRobotReal()
    {
        if (ros2CommandSender == null)
        {
            Debug.LogError("[FulcroSequenceManager] ros2CommandSender no asignado.");
            return;
        }

        StartCoroutine(SincronizarCoroutine());
    }

    private IEnumerator SincronizarCoroutine()
    {
        // Paso 1: mover robot real a pose home del fulcro
        if (ros2CommandSender != null)
        {
            ros2CommandSender.SendCommand("StopMotion()");
            ros2CommandSender.SendCommand("ResetAllError()");
            ros2CommandSender.SendCommand(
                $"JNTPoint(1,{homePoseJ1:F4},{homePoseJ2:F4},{homePoseJ3:F4}," +
                $"{homePoseJ4:F4},{homePoseJ5:F4},{homePoseJ6:F4})");
            ros2CommandSender.SendCommand($"MoveJ(JNT1,{velocidadSincronizacion:F0})");
            Debug.Log("[FulcroSequenceManager] Moviendo robot real a pose home.");
        }

        // Paso 2: mover robot virtual a la misma pose
        if (ikCalc != null && ikCalc.robot != null && ikCalc.robot.Count == 6)
        {
            float[] homeAngles = { homePoseJ1, homePoseJ2, homePoseJ3,
                                   homePoseJ4, homePoseJ5, homePoseJ6 };
            for (int i = 0; i < 6; i++)
                ikCalc.robot[i].localEulerAngles = ConvertirAngulo(
                    homeAngles[i] * Mathf.Deg2Rad, i);

            Debug.Log("[FulcroSequenceManager] Robot virtual en pose home.");
        }

        // Paso 3: esperar que el robot real llegue
        if (jointPositionSubscriber != null)
        {
            float[] target  = { homePoseJ1, homePoseJ2, homePoseJ3,
                                 homePoseJ4, homePoseJ5, homePoseJ6 };
            float timeout   = 30f;
            float elapsed   = 0f;
            bool  reached   = false;

            while (!reached && elapsed < timeout)
            {
                float[] current = jointPositionSubscriber.GetLastKnownPositions();
                if (current != null && current.Length >= 6)
                {
                    reached = true;
                    for (int i = 0; i < 6; i++)
                        if (Mathf.Abs(current[i] - target[i]) > 1f) { reached = false; break; }
                }
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            Debug.Log(reached
                ? "[FulcroSequenceManager] Pose home alcanzada."
                : "[FulcroSequenceManager] Timeout — continuando.");
        }
        else
            yield return new WaitForSeconds(3f);

        // Paso 4: guardar como punto de fulcro inicial
        _poseFulcroInicial     = new float[] { homePoseJ1, homePoseJ2, homePoseJ3,
                                               homePoseJ4, homePoseJ5, homePoseJ6 };
        _fulcroInicialDefinido = true;

        // Paso 5: actualizar goodSolution con la pose home
        // para que IK_calc no mueva el robot al activarse
        if (ikController != null)
            ikController.ActualizarReferencia();

        Debug.Log("[FulcroSequenceManager] Sincronización completada.");
    }

    public void CargarPunto(int index)
    {
        if (index < 0 || index >= _puntos.Count) return;
        FulcroPoint p = _puntos[index];
        if (ikCalc != null && ikCalc.ik != null)
        {
            ikCalc.ik.position = p.ikPosition;
            if (ikController != null) ikController.SyncSlidersFromTransform();
        }
    }

    // ------------------------------------------------------------------ //
    //  Ejecutar — bifurca según toggle
    // ------------------------------------------------------------------ //
    private void EjecutarSecuencia()
    {
        if (_puntos.Count == 0)
        {
            Debug.LogWarning("[FulcroSequenceManager] No hay puntos guardados.");
            return;
        }
        StartCoroutine(_modoSimulacion ? SimularSecuencia() : EnviarSecuencia());
    }

    // ------------------------------------------------------------------ //
    //  Helper: espera a que el usuario presione AVANZAR (modo espera)
    // ------------------------------------------------------------------ //
    private IEnumerator EsperarAvance()
    {
        if (!_modoEspera) yield break;

        _avanzar = false;
        if (btnAvanzar != null) btnAvanzar.interactable = true;
        Debug.Log("[FulcroSequenceManager] Esperando AVANZAR... (puedes usar MODO J6)");

        while (!_avanzar)
            yield return null;

        // Si MODO J6 estaba activo, desactivarlo antes de continuar
        // para que IK_calc se re-habilite y el fulcro vuelva a funcionar
        if (ikController != null && ikController.ModoJ6Activo)
        {
            ikController.DesactivarModoJ6();
            Debug.Log("[FulcroSequenceManager] MODO J6 desactivado al avanzar.");
            yield return null; // esperar un frame para que IK_calc se reactive
        }

        if (btnAvanzar != null) btnAvanzar.interactable = false;
        _avanzar = false;
    }

    // ------------------------------------------------------------------ //
    //  MODO VIRTUAL
    // ------------------------------------------------------------------ //
    private IEnumerator SimularSecuencia()
    {
        Debug.Log($"[FulcroSequenceManager] VIRTUAL: {_puntos.Count} puntos.");
        if (btnEjecutar != null) btnEjecutar.interactable = false;

        float currentSpeed = defaultSpeed;
        float duracion     = Mathf.Max(0.3f, simulacionPausa * (1f - (currentSpeed - 1f) / 99f * 0.75f));

        if (ikCalc == null || ikCalc.ik == null)
        {
            Debug.LogError("[FulcroSequenceManager] ikCalc o ikCalc.ik no asignado.");
            if (btnEjecutar != null) btnEjecutar.interactable = true;
            yield break;
        }

        for (int i = 0; i < _puntos.Count; i++)
        {
            FulcroPoint p          = _puntos[i];
            bool        tieneIKPos = p.ikPosition != Vector3.zero;

            if (tieneIKPos)
            {
                Vector3 posOrigen  = ikCalc.ik.position;
                Vector3 posDestino = p.ikPosition;
                float   elapsed    = 0f;

                while (elapsed < duracion)
                {
                    elapsed += Time.deltaTime;
                    float t       = Mathf.Clamp01(elapsed / duracion);
                    float tSmooth = t * t * (3f - 2f * t);
                    ikCalc.ik.position = Vector3.Lerp(posOrigen, posDestino, tSmooth);
                    if (ikController != null) ikController.SyncSlidersFromTransform();
                    yield return null;
                }
                ikCalc.ik.position = posDestino;
                if (ikController != null) ikController.SyncSlidersFromTransform();
            }
            else
            {
                if (ikCalc.robot == null || ikCalc.robot.Count < 6)
                {
                    yield return new WaitForSeconds(duracion);
                }
                else
                {
                    float[] angOrigen  = ObtenerAngulosActuales();
                    float[] angDestino = p.anglesRad;
                    float   elapsed    = 0f;

                    while (elapsed < duracion)
                    {
                        elapsed += Time.deltaTime;
                        float t       = Mathf.Clamp01(elapsed / duracion);
                        float tSmooth = t * t * (3f - 2f * t);
                        for (int j = 0; j < 6; j++)
                        {
                            float ang = Mathf.Lerp(angOrigen[j], angDestino[j], tSmooth);
                            ikCalc.robot[j].localEulerAngles = ConvertirAngulo(ang, j);
                        }
                        yield return null;
                    }
                    for (int j = 0; j < 6; j++)
                        ikCalc.robot[j].localEulerAngles = ConvertirAngulo(angDestino[j], j);
                }
            }

            // Esperar en este punto si modo espera está activo
            yield return StartCoroutine(EsperarAvance());

            yield return new WaitForSeconds(0.3f);
        }

        if (btnAvanzar != null) btnAvanzar.interactable = false;
        Debug.Log("[FulcroSequenceManager] Simulación virtual completada.");
        if (btnEjecutar != null) btnEjecutar.interactable = true;
    }

    // ------------------------------------------------------------------ //
    //  MODO ROBOT REAL — movimiento relativo basado en deltas articulares
    //
    //  En lugar de enviar ángulos absolutos (que dependen de que el virtual
    //  y el real estén perfectamente calibrados), calcula los DELTAS entre
    //  puntos consecutivos y los aplica sobre la posición actual del robot real.
    //
    //  Así el robot real hace exactamente el mismo movimiento relativo que
    //  el virtual, sin importar el desfase de altura u offset geométrico.
    // ------------------------------------------------------------------ //
    private IEnumerator EnviarSecuencia()
    {
        Debug.Log($"[FulcroSequenceManager] ROBOT REAL (relativo): {_puntos.Count} puntos.");
        if (btnEjecutar != null) btnEjecutar.interactable = false;

        if (jointPositionSubscriber != null)
            jointPositionSubscriber.StartUpdating();

        ros2CommandSender.SendCommand("DragTeachSwitch(0)");   yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand("StopMotion()");         yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand("ResetAllError()");      yield return new WaitForSeconds(0.05f);
        ros2CommandSender.SendCommand("StartJOG(0,6,0,100)"); yield return new WaitForSeconds(0.5f);
        ros2CommandSender.SendCommand("StartJOG(0,6,1,100)"); yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(0.5f);

        // Retornar al punto de fulcro inicial antes de ejecutar
        if (_fulcroInicialDefinido && _poseFulcroInicial != null)
        {
            float[] pf = _poseFulcroInicial;
            ros2CommandSender.SendCommand(
                $"JNTPoint(1,{pf[0]:F4},{pf[1]:F4},{pf[2]:F4},{pf[3]:F4},{pf[4]:F4},{pf[5]:F4})");
            yield return new WaitForSeconds(0.05f);
            ros2CommandSender.SendCommand($"MoveJ(JNT1,{velocidadSincronizacion:F0})");
            Debug.Log("[FulcroSequenceManager] Retornando al punto de fulcro inicial...");
            yield return StartCoroutine(WaitForRobotToReachPosition(_poseFulcroInicial, tolerance: 1f));
            Debug.Log("[FulcroSequenceManager] Punto de fulcro alcanzado. Iniciando secuencia.");
            yield return new WaitForSeconds(0.3f);
        }
        else
        {
            Debug.LogWarning("[FulcroSequenceManager] Punto de fulcro no definido. " +
                             "Pulsa SINCRONIZAR antes de ejecutar.");
        }

        // Referencias para cálculo de deltas
        float[] poseInicialReal = _fulcroInicialDefinido
            ? (float[])_poseFulcroInicial.Clone()
            : jointPositionSubscriber?.GetLastKnownPositions() ?? new float[6];

        float[] poseInicialVirtual = new float[6];
        if (IK_calc.goodSolution != null && IK_calc.goodSolution.Count >= 6)
            for (int j = 0; j < 6; j++)
                poseInicialVirtual[j] = Mathf.Rad2Deg * (float)IK_calc.goodSolution[j];
        else
            for (int j = 0; j < 6; j++)
                poseInicialVirtual[j] = Mathf.Rad2Deg * _puntos[0].anglesRad[j];

        Debug.Log($"[FulcroSequenceManager] Referencia real:    {string.Join(", ", poseInicialReal)}°");
        Debug.Log($"[FulcroSequenceManager] Referencia virtual: {string.Join(", ", poseInicialVirtual)}°");

        int batchSize  = 5;
        int numBatches = Mathf.CeilToInt(_puntos.Count / (float)batchSize);

        for (int batch = 0; batch < numBatches; batch++)
        {
            int startIdx = batch * batchSize;
            int endIdx   = Mathf.Min(startIdx + batchSize, _puntos.Count);

            // Calcular ángulos reales con deltas
            float[][] angulosReales = new float[endIdx - startIdx][];
            for (int i = startIdx; i < endIdx; i++)
            {
                int li = i - startIdx;
                angulosReales[li] = new float[6];
                for (int j = 0; j < 6; j++)
                {
                    float angVirtualDeg = Mathf.Rad2Deg * _puntos[i].anglesRad[j];
                    float delta         = angVirtualDeg - poseInicialVirtual[j];
                    angulosReales[li][j] = poseInicialReal[j] + delta;
                }
            }

            float currentSpeed = defaultSpeed;
            float duracion     = Mathf.Max(0.3f, simulacionPausa * (1f - (currentSpeed - 1f) / 99f * 0.75f));

            // Enviar cada punto: primero define JNTPoint, luego MoveJ inmediatamente
            // Esto garantiza que el robot ejecuta los puntos en orden correcto
            for (int i = 0; i < angulosReales.Length; i++)
            {
                int         li       = i + 1;
                float[]     ar       = angulosReales[i];
                float       spd      = _puntos[startIdx + i].speed;
                FulcroPoint punto    = _puntos[startIdx + i];
                bool        tieneIK  = punto.ikPosition != Vector3.zero;

                // 1. Definir el punto
                ros2CommandSender.SendCommand(
                    $"JNTPoint({li},{ar[0]:F4},{ar[1]:F4},{ar[2]:F4},{ar[3]:F4},{ar[4]:F4},{ar[5]:F4})");
                Debug.Log($"[FulcroSequenceManager] JNTPoint({li}): " +
                          $"{ar[0]:F2} {ar[1]:F2} {ar[2]:F2} {ar[3]:F2} {ar[4]:F2} {ar[5]:F2}°");
                yield return new WaitForSeconds(0.05f);

                // 2. Mover al punto inmediatamente
                ros2CommandSender.SendCommand($"MoveJ(JNT{li},{spd:F0})");
                yield return new WaitForSeconds(0.05f);

                // 3. Mover robot virtual en paralelo mientras el real se desplaza
                if (tieneIK && ikCalc != null && ikCalc.ik != null)
                {
                    Vector3 posOrigen  = ikCalc.ik.position;
                    Vector3 posDestino = punto.ikPosition;
                    float   elapsed    = 0f;

                    while (elapsed < duracion)
                    {
                        elapsed += Time.deltaTime;
                        float t       = Mathf.Clamp01(elapsed / duracion);
                        float tSmooth = t * t * (3f - 2f * t);
                        ikCalc.ik.position = Vector3.Lerp(posOrigen, posDestino, tSmooth);
                        if (ikController != null) ikController.SyncSlidersFromTransform();
                        yield return null;
                    }
                    ikCalc.ik.position = posDestino;
                    if (ikController != null) ikController.SyncSlidersFromTransform();
                }
                else if (ikCalc != null && ikCalc.robot != null && ikCalc.robot.Count == 6)
                {
                    float[] angOrigen  = ObtenerAngulosActuales();
                    float[] angDestino = punto.anglesRad;
                    float   elapsed    = 0f;

                    while (elapsed < duracion)
                    {
                        elapsed += Time.deltaTime;
                        float t       = Mathf.Clamp01(elapsed / duracion);
                        float tSmooth = t * t * (3f - 2f * t);
                        for (int j = 0; j < 6; j++)
                        {
                            float ang = Mathf.Lerp(angOrigen[j], angDestino[j], tSmooth);
                            ikCalc.robot[j].localEulerAngles = ConvertirAngulo(ang, j);
                        }
                        yield return null;
                    }
                    for (int j = 0; j < 6; j++)
                        ikCalc.robot[j].localEulerAngles = ConvertirAngulo(angDestino[j], j);
                }

                // 4. Esperar que el robot real llegue antes del siguiente punto
                yield return StartCoroutine(
                    WaitForRobotToReachPosition(ar, tolerance: 1f));

                Debug.Log($"[FulcroSequenceManager] Robot alcanzó JNT{li}.");

                // Esperar en este punto si modo espera está activo
                yield return StartCoroutine(EsperarAvance());

                yield return new WaitForSeconds(0.3f);
            }

            yield return new WaitForSeconds(0.1f);
        }

        if (btnAvanzar != null) btnAvanzar.interactable = false;
        Debug.Log("[FulcroSequenceManager] Secuencia relativa completada.");
        if (btnEjecutar != null) btnEjecutar.interactable = true;
    }

    // ------------------------------------------------------------------ //
    //  Esperar a que el robot alcance la posición objetivo
    //  Mismo patrón que ControlArticular.WaitForRobotToReachPosition
    // ------------------------------------------------------------------ //
    private IEnumerator WaitForRobotToReachPosition(float[] targetDeg, float tolerance)
    {
        if (jointPositionSubscriber == null)
        {
            Debug.LogWarning("[FulcroSequenceManager] JointPositionSubscriber no asignado. " +
                             "Esperando tiempo fijo de 2s.");
            yield return new WaitForSeconds(2f);
            yield break;
        }

        bool  reached     = false;
        float timeout     = 30f;
        float elapsedTime = 0f;

        while (!reached && elapsedTime < timeout)
        {
            float[] current = jointPositionSubscriber.GetLastKnownPositions();

            if (current == null || current.Length != targetDeg.Length)
            {
                yield return new WaitForSeconds(0.1f);
                elapsedTime += 0.1f;
                continue;
            }

            reached = true;
            for (int i = 0; i < targetDeg.Length; i++)
            {
                if (Mathf.Abs(current[i] - targetDeg[i]) > tolerance)
                {
                    reached = false;
                    break;
                }
            }

            if (!reached)
            {
                yield return new WaitForSeconds(0.1f);
                elapsedTime += 0.1f;
            }
        }

        if (elapsedTime >= timeout)
            Debug.LogWarning("[FulcroSequenceManager] Timeout alcanzado. Continuando.");
    }

    // ------------------------------------------------------------------ //
    //  Helpers de interpolación de joints
    // ------------------------------------------------------------------ //
    private float[] ObtenerAngulosActuales()
    {
        float[] angulos = new float[6];
        if (ikCalc == null || ikCalc.robot == null) return angulos;
        for (int i = 0; i < 6 && i < ikCalc.robot.Count; i++)
        {
            Vector3 euler = ikCalc.robot[i].localEulerAngles;
            float deg = i switch
            {
                0 => -euler.y, 1 => -euler.x, 2 => -euler.y,
                3 => -euler.y, 4 => -euler.x, 5 =>  euler.x,
                _ => 0f
            };
            if (deg > 180f)  deg -= 360f;
            if (deg < -180f) deg += 360f;
            angulos[i] = deg * Mathf.Deg2Rad;
        }
        return angulos;
    }

    private Vector3 ConvertirAngulo(float angleRad, int jointIndex)
    {
        float deg = angleRad * Mathf.Rad2Deg;
        return jointIndex switch
        {
            0 => new Vector3(0,    -deg,    0),
            1 => new Vector3(-deg,  0,    -90),
            2 => new Vector3(0,    -deg,    0),
            3 => new Vector3(0,    -deg,    0),
            4 => new Vector3(-deg,  0,    -90),
            5 => new Vector3( deg,  0,     90),
            _ => Vector3.zero
        };
    }

    // ------------------------------------------------------------------ //
    //  Guardar en .txt
    //  Línea 1: "fulcro"
    //  Siguientes: j1,j2,j3,j4,j5,j6,speed,0,ikX,ikY,ikZ
    // ------------------------------------------------------------------ //
    private void GuardarEnTxt()
    {
        if (_puntos.Count == 0)
        {
            Debug.LogWarning("[FulcroSequenceManager] No hay puntos para guardar.");
            return;
        }

        string filePath = Path.Combine(savePath, fileName);
        try
        {
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("fulcro");
                foreach (FulcroPoint p in _puntos)
                {
                    float j1 = Mathf.Rad2Deg * p.anglesRad[0];
                    float j2 = Mathf.Rad2Deg * p.anglesRad[1];
                    float j3 = Mathf.Rad2Deg * p.anglesRad[2];
                    float j4 = Mathf.Rad2Deg * p.anglesRad[3];
                    float j5 = Mathf.Rad2Deg * p.anglesRad[4];
                    float j6 = Mathf.Rad2Deg * p.anglesRad[5];
                    writer.WriteLine(
                        $"{j1:F4},{j2:F4},{j3:F4},{j4:F4},{j5:F4},{j6:F4}," +
                        $"{p.speed:F0},0," +
                        $"{p.ikPosition.x:F6},{p.ikPosition.y:F6},{p.ikPosition.z:F6}");
                }
            }
            Debug.Log($"[FulcroSequenceManager] Guardado en: {filePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FulcroSequenceManager] Error al guardar: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ //
    //  Cargar desde .txt
    //  Formato: j1,j2,j3,j4,j5,j6,speed,delay[,ikX,ikY,ikZ]
    // ------------------------------------------------------------------ //
    private void CargarDesdeTxt()
    {
        string filePath = Path.Combine(savePath, fileName);

        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[FulcroSequenceManager] Archivo no encontrado: {filePath}");
            if (listaDisplay != null) listaDisplay.text = "Error: archivo no encontrado.";
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return;

            if (lines[0].Trim().ToLower() != "fulcro")
            {
                if (listaDisplay != null) listaDisplay.text = "Error: no es una secuencia de fulcro.";
                return;
            }

            _puntos.Clear();
            int cargados = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split(',');
                if (parts.Length < 7) continue;

                FulcroPoint punto = new FulcroPoint
                {
                    anglesRad  = new float[6],
                    speed      = float.Parse(parts[6]),
                    ikPosition = Vector3.zero
                };

                for (int j = 0; j < 6; j++)
                    punto.anglesRad[j] = float.Parse(parts[j]) * Mathf.Deg2Rad;

                // ikX, ikY, ikZ — posición mundial (columnas 8,9,10)
                if (parts.Length >= 11)
                {
                    punto.ikPosition = new Vector3(
                        float.Parse(parts[8]),
                        float.Parse(parts[9]),
                        float.Parse(parts[10]));
                }

                _puntos.Add(punto);
                cargados++;
            }

            Debug.Log($"[FulcroSequenceManager] {cargados} puntos cargados.");
            UpdateButtonStates();
            ActualizarDisplay();

            if (listaDisplay != null && cargados > 0)
                listaDisplay.text = $"✓ {cargados} puntos cargados.\n\n" + listaDisplay.text;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FulcroSequenceManager] Error al cargar: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ //
    //  Explorador nativo Linux (zenity)
    // ------------------------------------------------------------------ //
    private void AbrirExplorador()
    {
        StartCoroutine(AbrirExplotadorCoroutine());
    }

    private IEnumerator AbrirExplotadorCoroutine()
    {
        if (btnSeleccionarTxt != null) btnSeleccionarTxt.interactable = false;

        string archivoSeleccionado = null;

        System.Threading.Thread thread = new System.Threading.Thread(() =>
        {
            try
            {
                var proceso = new System.Diagnostics.Process();
                proceso.StartInfo.FileName               = "zenity";
                proceso.StartInfo.Arguments              =
                    $"--file-selection " +
                    $"--title=\"Seleccionar secuencia de Fulcro\" " +
                    $"--filename=\"{savePath}/\" " +
                    $"--file-filter=\"Archivos TXT | *.txt\"";
                proceso.StartInfo.UseShellExecute        = false;
                proceso.StartInfo.RedirectStandardOutput = true;
                proceso.StartInfo.RedirectStandardError  = true;
                proceso.StartInfo.CreateNoWindow         = true;
                proceso.Start();
                string output = proceso.StandardOutput.ReadToEnd().Trim();
                proceso.WaitForExit();
                if (proceso.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    archivoSeleccionado = output;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FulcroSequenceManager] Error zenity: {e.Message}");
            }
        });

        thread.Start();
        while (thread.IsAlive) yield return null;

        if (btnSeleccionarTxt != null) btnSeleccionarTxt.interactable = true;

        if (!string.IsNullOrEmpty(archivoSeleccionado))
        {
            savePath = Path.GetDirectoryName(archivoSeleccionado);
            fileName = Path.GetFileName(archivoSeleccionado);
            CargarDesdeTxt();
        }
    }

    // ------------------------------------------------------------------ //
    //  Display
    // ------------------------------------------------------------------ //
    private void ActualizarDisplay()
    {
        string texto;

        if (_puntos.Count == 0)
        {
            texto = "Sin puntos guardados";
        }
        else
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < _puntos.Count; i++)
            {
                FulcroPoint p  = _puntos[i];
                float j1 = Mathf.Rad2Deg * p.anglesRad[0];
                float j2 = Mathf.Rad2Deg * p.anglesRad[1];
                float j3 = Mathf.Rad2Deg * p.anglesRad[2];
                float j4 = Mathf.Rad2Deg * p.anglesRad[3];
                float j5 = Mathf.Rad2Deg * p.anglesRad[4];
                float j6 = Mathf.Rad2Deg * p.anglesRad[5];
                bool  tieneIK = p.ikPosition != Vector3.zero;
                sb.AppendLine(
                    $"P{i+1}  Speed:{p.speed:F0}  {(tieneIK ? "IK+" : "IK-")}\n" +
                    $"  J1:{j1:F1} J2:{j2:F1} J3:{j3:F1}\n" +
                    $"  J4:{j4:F1} J5:{j5:F1} J6:{j6:F1}");
            }
            texto = sb.ToString();
        }

        // Escribir en el componente disponible
        if (listaDisplay        != null) listaDisplay.text        = texto;
        if (listaDisplayLegacy  != null) listaDisplayLegacy.text  = texto;
    }

    // ------------------------------------------------------------------ //
    //  Estado de botones
    // ------------------------------------------------------------------ //
    private void UpdateButtonStates()
    {
        bool hay = _puntos.Count > 0;
        if (btnEliminar   != null) btnEliminar.interactable   = hay;
        if (btnEjecutar   != null) btnEjecutar.interactable   = hay;
        if (btnGuardarTxt != null) btnGuardarTxt.interactable = hay;
    }
}