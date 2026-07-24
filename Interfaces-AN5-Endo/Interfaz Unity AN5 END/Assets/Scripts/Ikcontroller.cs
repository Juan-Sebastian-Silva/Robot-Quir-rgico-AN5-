using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// IKController
///
/// Mueve el objeto IK en coordenadas MUNDIALES (position, no localPosition).
/// Esto evita la deformación errática que ocurre cuando el IK es hijo de un
/// link del robot y sus coordenadas locales cambian con la pose del robot.
///
/// Los sliders muestran el desplazamiento en cm respecto a la posición
/// de referencia capturada al activar el panel (OnEnable).
/// </summary>
public class IKController : MonoBehaviour
{
    [Header("Objeto a mover")]
    [Tooltip("El Transform del objeto vacío IK en la escena")]
    public Transform ikTransform;

    [Header("Control de IK_calc")]
    [Tooltip("Referencia a IK_calc — se habilita automáticamente al abrir este panel.")]
    public IK_calc ikCalc;

    [Header("Sliders de posición XYZ")]
    public Slider sliderX;
    public Slider sliderY;
    public Slider sliderZ;

    [Header("Labels de posición")]
    public TMP_Text labelPosX;
    public TMP_Text labelPosY;
    public TMP_Text labelPosZ;

    [Header("Rango de desplazamiento (centímetros)")]
    public float rangoX = 10f;
    public float rangoY = 10f;
    public float rangoZ = 10f;

    [Header("Límites asimétricos (opcional — sobreescribe rango si > 0)")]
    public float limMinX = 0f;
    public float limMaxX = 0f;
    public float limMinY = 0f;
    public float limMaxY = 0f;
    public float limMinZ = 0f;
    public float limMaxZ = 0f;

    [Header("Slider J6 — rotación cámara")]
    [Tooltip("Slider para rotar J6 independientemente del IK (solo J6 se mueve)")]
    public Slider   sliderJ6;
    [Range(10f, 180f)]
    public float    rangoJ6 = 90f;
    public TMP_Text labelJ6;
    public Text     labelJ6Legacy;
    [Tooltip("Ros2CommandSender para enviar J6 al robot real al mover el slider")]
    public Ros2CommandSender ros2CommandSenderJ6;
    [Range(1, 20)]
    public float velocidadJ6 = 5f;
    [Tooltip("Segundos que espera para que el robot complete el movimiento J6 antes de aceptar otro.\n" +
             "Auméntalo si el robot se bloquea. A velocidad 5 usa ~2s.")]
    [Range(0.5f, 10f)]
    public float esperaMovimientoJ6 = 2f;

    [Header("Modo J6 — botón de activación")]
    [Tooltip("Botón que activa/desactiva el modo J6 (sale del fulcro, solo mueve J6)")]
    public Button   btnModoJ6;
    [Tooltip("JointPositionSubscriber para leer posición actual del robot real")]
    public JointPositionSubscriber jointPositionSubscriber;
    [Tooltip("Texto del botón para indicar estado")]
    public Text     labelModoJ6;

    private bool     _modoJ6Activo = false;

    /// <summary>Indica si el modo J6 está activo actualmente.</summary>
    public bool ModoJ6Activo => _modoJ6Activo;

    /// <summary>Desactiva el modo J6 si está activo — llamado por FulcroSequenceManager al avanzar.</summary>
    public void DesactivarModoJ6()
    {
        if (!_modoJ6Activo) return;
        ToggleModoJ6(); // reutiliza la misma lógica de desactivación
    }

    private Vector3   _refPosition;
    private bool      _updating         = false;
    private float     _j6OffsetDeg      = 0f;
    private float     _j6UltimoEnv      = 0f;
    private bool      _j6Updating       = false;
    private bool      _j6Listo          = true;      // false mientras robot ejecuta MoveJ
    private float     _j6PendienteDeg   = float.NaN; // último valor pedido mientras ocupado
    private Coroutine _j6SendCoroutine  = null;
    private float[]   _baseAnglesJ6     = new float[6];

    // ------------------------------------------------------------------ //
    void Awake()
    {
        if (sliderX  != null) sliderX.onValueChanged.AddListener(OnSliderX);
        if (sliderY  != null) sliderY.onValueChanged.AddListener(OnSliderY);
        if (sliderZ  != null) sliderZ.onValueChanged.AddListener(OnSliderZ);
        if (sliderJ6 != null) sliderJ6.onValueChanged.AddListener(OnSliderJ6);
        // btnModoJ6 se asigna directamente desde el Inspector — no registrar aquí
    }

    void Start()
    {
        if (ikTransform == null)
            Debug.LogError("[IKController] ikTransform no asignado en el Inspector.");
    }

    void OnEnable()
    {
        if (ikTransform == null) return;

        _refPosition = ikTransform.position;

        _updating = true;
        ConfigurarSlider(sliderX, rangoX, limMinX, limMaxX, 0f);
        ConfigurarSlider(sliderY, rangoY, limMinY, limMaxY, 0f);
        ConfigurarSlider(sliderZ, rangoZ, limMinZ, limMaxZ, 0f);
        _updating = false;

        // Resetear slider J6 — deshabilitado hasta pulsar MODO J6
        _j6Updating  = true;
        _j6OffsetDeg = 0f;
        _j6UltimoEnv = 0f;
        _modoJ6Activo = false;
        if (sliderJ6 != null)
        {
            sliderJ6.minValue     = -rangoJ6;
            sliderJ6.maxValue     =  rangoJ6;
            sliderJ6.value        = 0f;
            sliderJ6.interactable = false; // se activa solo al pulsar el botón
        }
        if (labelModoJ6 != null) labelModoJ6.text = "MODO J6";
        _j6Updating = false;

        if (ikCalc != null) ikCalc.enabled = true;
        ActualizarLabels();
    }

    // Aplica el offset de J6 después de que IK_calc.Update() corrió
    void LateUpdate()
    {
        if (!enabled || ikCalc == null || ikCalc.robot == null ||
            ikCalc.robot.Count < 6 || _j6OffsetDeg == 0f) return;

        float baseJ6 = (IK_calc.goodSolution != null && IK_calc.goodSolution.Count >= 6)
            ? Mathf.Rad2Deg * (float)IK_calc.goodSolution[5]
            : 0f;

        ikCalc.robot[5].localEulerAngles = new Vector3(baseJ6 + _j6OffsetDeg, 0, 90);
    }

    void OnDisable()
    {
        if (ikCalc != null) ikCalc.enabled = false;
    }

    // ------------------------------------------------------------------ //
    //  Callbacks — valor en cm → desplazamiento mundial en metros
    // ------------------------------------------------------------------ //
    private void OnSliderX(float valueCm)
    {
        if (_updating || ikTransform == null) return;
        Vector3 pos = ikTransform.position;
        pos.x = _refPosition.x + (valueCm / 100f);
        ikTransform.position = pos;
        ActualizarLabels();
    }

    private void OnSliderY(float valueCm)
    {
        if (_updating || ikTransform == null) return;
        Vector3 pos = ikTransform.position;
        pos.y = _refPosition.y + (valueCm / 100f);
        ikTransform.position = pos;
        ActualizarLabels();
    }

    private void OnSliderZ(float valueCm)
    {
        if (_updating || ikTransform == null) return;
        Vector3 pos = ikTransform.position;
        pos.z = _refPosition.z + (valueCm / 100f);
        ikTransform.position = pos;
        ActualizarLabels();
    }

    // ------------------------------------------------------------------ //
    //  Slider J6 — solo rota J6, J1-J5 permanecen fijos
    // ------------------------------------------------------------------ //
    private void OnSliderJ6(float value)
    {
        if (_j6Updating || !_modoJ6Activo) return;
        _j6OffsetDeg = value;

        string txt = $"J6: {value:F1}°";
        if (labelJ6       != null) labelJ6.text       = txt;
        if (labelJ6Legacy != null) labelJ6Legacy.text = txt;

        if (ros2CommandSenderJ6 == null) return;

        float j6Real = _baseAnglesJ6[5] + value;

        if (_j6Listo)
        {
            // Robot libre — enviar con debounce
            if (_j6SendCoroutine != null) StopCoroutine(_j6SendCoroutine);
            _j6SendCoroutine = StartCoroutine(EnviarJ6ConDebounce(j6Real));
        }
        else
        {
            // Robot ocupado — guardar el último valor para enviar cuando termine
            _j6PendienteDeg = j6Real;
        }
    }

    private IEnumerator EnviarJ6ConDebounce(float j6RealDeg)
    {
        // Debounce: esperar a que el usuario deje de mover el slider
        yield return new WaitForSeconds(0.4f);
        if (ros2CommandSenderJ6 == null) yield break;

        // Bloquear nuevos envíos mientras el robot se mueve
        _j6Listo        = false;
        _j6PendienteDeg = float.NaN;

        ros2CommandSenderJ6.SendCommand(
            $"JNTPoint(1,{_baseAnglesJ6[0]:F4},{_baseAnglesJ6[1]:F4},{_baseAnglesJ6[2]:F4}," +
            $"{_baseAnglesJ6[3]:F4},{_baseAnglesJ6[4]:F4},{j6RealDeg:F4})");
        ros2CommandSenderJ6.SendCommand($"MoveJ(JNT1,{velocidadJ6:F0})");

        Debug.Log($"[IKController] J6 enviado: {j6RealDeg:F1}° | esperando {esperaMovimientoJ6}s");

        // Esperar a que el robot complete el movimiento
        yield return new WaitForSeconds(esperaMovimientoJ6);

        _j6Listo         = true;
        _j6UltimoEnv     = _j6OffsetDeg;
        _j6SendCoroutine = null;

        // Si el slider se movió mientras esperábamos, enviar el valor pendiente
        if (!float.IsNaN(_j6PendienteDeg))
        {
            float pending   = _j6PendienteDeg;
            _j6PendienteDeg = float.NaN;
            _j6SendCoroutine = StartCoroutine(EnviarJ6ConDebounce(pending));
        }
    }

    // ------------------------------------------------------------------ //
    //  Modo J6 — activa/desactiva el control exclusivo de J6
    //  Al activar:
    //   1. Lee posición REAL actual del robot vía JointPositionSubscriber
    //      → J1-J5 fijos desde la pose real (no desde IK calculado)
    //   2. Deshabilita IK_calc (sale del fulcro temporalmente)
    //   3. Habilita el slider J6
    //  Al desactivar:
    //   1. Re-habilita IK_calc
    //   2. Resetea slider J6
    // ------------------------------------------------------------------ //
    public void ToggleModoJ6()
    {
        _modoJ6Activo = !_modoJ6Activo;
        Debug.Log($"[IKController] ToggleModoJ6 llamado → _modoJ6Activo = {_modoJ6Activo}");

        if (_modoJ6Activo)
        {
            // Leer posición REAL del robot como base fija para J1-J5
            bool datosReales = false;
            if (jointPositionSubscriber != null)
            {
                float[] posReal = null;
                try { posReal = jointPositionSubscriber.GetLastKnownPositions(); } catch { }

                if (posReal != null && posReal.Length >= 6)
                {
                    bool tieneValores = false;
                    foreach (float v in posReal)
                        if (Mathf.Abs(v) > 0.1f) { tieneValores = true; break; }

                    if (tieneValores)
                    {
                        for (int i = 0; i < 6; i++)
                            _baseAnglesJ6[i] = posReal[i];
                        datosReales = true;
                        Debug.Log($"[IKController] Base real: J1:{posReal[0]:F1} J2:{posReal[1]:F1} " +
                                  $"J3:{posReal[2]:F1} J4:{posReal[3]:F1} J5:{posReal[4]:F1} J6:{posReal[5]:F1}°");
                    }
                }
            }

            if (!datosReales)
            {
                if (IK_calc.goodSolution != null && IK_calc.goodSolution.Count >= 6)
                    for (int i = 0; i < 6; i++)
                        _baseAnglesJ6[i] = Mathf.Rad2Deg * (float)IK_calc.goodSolution[i];
                Debug.LogWarning("[IKController] Sin datos reales — usando goodSolution.");
            }

            // Deshabilitar IK_calc
            if (ikCalc != null) ikCalc.enabled = false;

            // Activar slider J6
            _j6Updating     = true;
            _j6OffsetDeg    = 0f;
            _j6Listo        = true;
            _j6PendienteDeg = float.NaN;
            if (sliderJ6 != null)
            {
                sliderJ6.minValue     = -rangoJ6;
                sliderJ6.maxValue     =  rangoJ6;
                sliderJ6.value        = 0f;
                sliderJ6.interactable = true;
                Debug.Log($"[IKController] sliderJ6.interactable = {sliderJ6.interactable}");
            }
            else
            {
                Debug.LogError("[IKController] sliderJ6 NO está asignado en el Inspector.");
            }
            _j6Updating = false;

            if (labelModoJ6 != null) labelModoJ6.text = "MODO J6 ON";
        }
        else
        {
            if (_j6SendCoroutine != null)
            {
                StopCoroutine(_j6SendCoroutine);
                _j6SendCoroutine = null;
            }

            if (ikCalc != null) ikCalc.enabled = true;

            _j6Updating  = true;
            _j6OffsetDeg = 0f;
            if (sliderJ6 != null)
            {
                sliderJ6.value        = 0f;
                sliderJ6.interactable = false;
            }
            _j6Updating = false;

            if (labelModoJ6 != null) labelModoJ6.text = "MODO J6";
            Debug.Log("[IKController] Modo J6 OFF — fulcro restaurado.");
        }
    }

    // ------------------------------------------------------------------ //
    //  Configurar slider — usa límites asimétricos si están definidos
    // ------------------------------------------------------------------ //
    private void ConfigurarSlider(Slider slider, float rango, float limMin, float limMax, float initial)
    {
        if (slider == null) return;
        // Si los límites asimétricos están definidos (> 0), usarlos
        float min = (limMin != 0f || limMax != 0f) ? -Mathf.Abs(limMin) : -rango;
        float max = (limMin != 0f || limMax != 0f) ?  Mathf.Abs(limMax) :  rango;
        slider.minValue     = min;
        slider.maxValue     = max;
        slider.wholeNumbers = false;
        slider.value        = Mathf.Clamp(initial, min, max);
    }

    // ------------------------------------------------------------------ //
    //  Labels — muestra desplazamiento en cm respecto a la referencia
    // ------------------------------------------------------------------ //
    public void ActualizarLabels()
    {
        if (ikTransform == null) return;
        Vector3 delta = (ikTransform.position - _refPosition) * 100f;
        if (labelPosX != null) labelPosX.text = $"X: {delta.x:F2} cm";
        if (labelPosY != null) labelPosY.text = $"Y: {delta.y:F2} cm";
        if (labelPosZ != null) labelPosZ.text = $"Z: {delta.z:F2} cm";
    }

    // ------------------------------------------------------------------ //
    //  Reset — vuelve a la posición de referencia
    // ------------------------------------------------------------------ //
    public void ResetPosition()
    {
        if (ikTransform == null) return;

        ikTransform.position = _refPosition;

        _updating = true;
        if (sliderX != null) sliderX.value = 0f;
        if (sliderY != null) sliderY.value = 0f;
        if (sliderZ != null) sliderZ.value = 0f;
        _updating = false;

        ActualizarLabels();
        Debug.Log("[IKController] IK reseteado a posición de referencia.");
    }

    // ------------------------------------------------------------------ //
    //  ActualizarReferencia — captura la posición actual del IK como
    //  nuevo punto de referencia y centra los sliders en 0.
    //  Llamado por FulcroCommandSender al activar el fulcro para que
    //  el robot no se mueva desde la pose sincronizada.
    // ------------------------------------------------------------------ //
    public void ActualizarReferencia()
    {
        if (ikTransform == null) return;

        _refPosition = ikTransform.position;

        _updating = true;
        if (sliderX != null) { sliderX.minValue = -rangoX; sliderX.maxValue = rangoX; sliderX.value = 0f; }
        if (sliderY != null) { sliderY.minValue = -rangoY; sliderY.maxValue = rangoY; sliderY.value = 0f; }
        if (sliderZ != null) { sliderZ.minValue = -rangoZ; sliderZ.maxValue = rangoZ; sliderZ.value = 0f; }
        _updating = false;

        // Resetear J6 y recapturar base angles
        _j6OffsetDeg = 0f;
        _j6UltimoEnv = 0f;
        _j6Updating  = true;
        if (sliderJ6 != null) sliderJ6.value = 0f;
        _j6Updating  = false;

        if (IK_calc.goodSolution != null && IK_calc.goodSolution.Count >= 6)
            for (int i = 0; i < 6; i++)
                _baseAnglesJ6[i] = Mathf.Rad2Deg * (float)IK_calc.goodSolution[i];

        ActualizarLabels();
        Debug.Log($"[IKController] Nueva referencia: {_refPosition} | Base J6: {_baseAnglesJ6[5]:F2}°");
    }

    // ------------------------------------------------------------------ //
    //  Sincronizar sliders desde código (al cargar un punto guardado)
    // ------------------------------------------------------------------ //
    public void SyncSlidersFromTransform()
    {
        if (ikTransform == null) return;

        Vector3 delta = (ikTransform.position - _refPosition) * 100f;

        _updating = true;
        if (sliderX != null) sliderX.value = Mathf.Clamp(delta.x, -rangoX, rangoX);
        if (sliderY != null) sliderY.value = Mathf.Clamp(delta.y, -rangoY, rangoY);
        if (sliderZ != null) sliderZ.value = Mathf.Clamp(delta.z, -rangoZ, rangoZ);
        _updating = false;

        ActualizarLabels();
    }
}