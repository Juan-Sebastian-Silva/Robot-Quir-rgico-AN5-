using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// FulcroPanel
///
/// Conecta los controles UI con FulcroCommandSender.
/// Al abrirse (OnEnable), sincroniza automáticamente el robot virtual
/// con la posición actual del robot real leyendo JointPositionSubscriber.
///
/// CORRECCIÓN: Se eliminó el filtro que ignoraba posiciones cuando todos los
/// joints reportan 0°, ya que home position (todos en cero) es válido y el
/// filtro anterior impedía la sincronización al arrancar desde home.
/// </summary>
public class FulcroPanel : MonoBehaviour
{
    [Header("Referencia al sender")]
    public FulcroCommandSender ros2Sender;

    [Header("Sincronización robot real → virtual")]
    [Tooltip("JointPositionSubscriber para leer posición actual del robot real")]
    public JointPositionSubscriber jointPositionSubscriber;
    [Tooltip("JointStateWriters del robot virtual (J1..J6)")]
    public JointStateWriter[] jointStateWriters;

    [Header("Controles UI")]
    public Toggle   toggleModo;
    public Slider   speedSlider;
    public TMP_Text speedValueLabel;
    public Button   btnEnviar;

    [Header("Indicador de estado")]
    public Image    statusIndicator;
    public TMP_Text statusLabel;

    [Header("Colores")]
    public Color colorActivo   = new Color(0.30f, 1.00f, 0.57f);
    public Color colorInactivo = new Color(0.47f, 0.53f, 0.67f);

    // ------------------------------------------------------------------ //
    void Start()
    {
        if (toggleModo != null)
        {
            toggleModo.isOn = false;
            toggleModo.onValueChanged.AddListener(OnToggleChanged);
        }

        if (speedSlider != null)
        {
            speedSlider.minValue     = 1f;
            speedSlider.maxValue     = 100f;
            speedSlider.wholeNumbers = true;
            speedSlider.value        = 10f;
            speedSlider.onValueChanged.AddListener(OnSpeedChanged);
            UpdateSpeedLabel(10f);
        }

        if (btnEnviar != null)
        {
            btnEnviar.onClick.AddListener(OnEnviarPressed);
            btnEnviar.interactable = false;
        }

        UpdateStatusIndicator(false);
    }

    // ------------------------------------------------------------------ //
    //  OnEnable — sincronizar robot virtual con robot real al abrir panel
    // ------------------------------------------------------------------ //
    void OnEnable()
    {
        SincronizarConRobotReal();
    }

    /// <summary>
    /// Lee la posición actual del robot real y la aplica al robot virtual.
    ///
    /// CORRECCIÓN respecto a la versión anterior:
    /// Se eliminó el bloque que ignoraba la sincronización cuando todos los
    /// valores eran 0° (comprobación !hayDatos). Esa lógica era incorrecta:
    /// si el robot real está en home (todos los joints en 0°), es una posición
    /// perfectamente válida y debe sincronizarse. El filtro anterior provocaba
    /// que el robot virtual nunca se actualizara al arrancar desde home.
    ///
    /// Ahora solo se verifica que el subscriber exista y que el array no sea nulo.
    /// Si no hay conexión ROS y el subscriber devuelve un array vacío, tampoco
    /// se aplica nada (comportamiento correcto offline).
    /// </summary>
    private void SincronizarConRobotReal()
    {
        if (jointPositionSubscriber == null || jointStateWriters == null) return;

        float[] posActual = jointPositionSubscriber.GetLastKnownPositions();

        // Solo verificar que el array existe con datos — no filtrar por ceros,
        // porque home position (todos en 0°) es un estado perfectamente válido.
        if (posActual == null || posActual.Length == 0)
        {
            Debug.Log("[FulcroPanel] Sin datos del robot real — robot virtual sin cambios.");
            return;
        }

        // Aplicar posición real al robot virtual
        for (int i = 0; i < jointStateWriters.Length && i < posActual.Length; i++)
        {
            if (jointStateWriters[i] != null)
                jointStateWriters[i].Write(posActual[i] * Mathf.Deg2Rad);
        }

        Debug.Log($"[FulcroPanel] Robot virtual sincronizado con robot real: " +
                  $"{string.Join(", ", posActual)}°");
    }

    // ------------------------------------------------------------------ //

    private void OnToggleChanged(bool isOn)
    {
        if (ros2Sender != null)
            ros2Sender.SetModeActive(isOn);

        if (btnEnviar != null)
            btnEnviar.interactable = isOn;

        UpdateStatusIndicator(isOn);
    }

    private void OnSpeedChanged(float value)
    {
        UpdateSpeedLabel(value);
        if (ros2Sender != null)
            ros2Sender.speed = value;
    }

    private void OnEnviarPressed()
    {
        if (ros2Sender != null)
            ros2Sender.SendFromPanel();
    }

    // ------------------------------------------------------------------ //

    private void UpdateStatusIndicator(bool isActive)
    {
        if (statusIndicator != null)
            statusIndicator.color = isActive ? colorActivo : colorInactivo;
        if (statusLabel != null)
            statusLabel.text = isActive ? "ACTIVO" : "INACTIVO";
    }

    private void UpdateSpeedLabel(float value)
    {
        if (speedValueLabel != null)
            speedValueLabel.text = ((int)value).ToString();
    }
}