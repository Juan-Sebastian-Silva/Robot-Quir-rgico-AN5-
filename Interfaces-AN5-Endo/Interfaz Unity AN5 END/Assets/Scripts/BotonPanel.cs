/*******************
Autores:    Angel Garzon Sarzosa (ahgarzon@unicauca.edu.co)
            Jhoan Simei Sarria (simei@unicauca.edu.co)
*******************/
using UnityEngine.UI;
using UnityEngine;

public class ButtonPanel : MonoBehaviour
{
    public GameObject cartesianPanel;
    public GameObject jointPanel;
    public GameObject recordPanel;
    public GameObject txtPanel;

    [Header("Panel Fulcro")]
    public GameObject fulcroPanel;

    [Header("Botones del panel principal a ocultar en modo Fulcro")]
    [Tooltip("Asignar los GameObjects de los botones ARTICULAR, CARTESIAN, RECORD y TXT")]
    public GameObject btnArticular;
    public GameObject btnCartesian;
    public GameObject btnRecord;
    public GameObject btnTxt;

    public GameObject manualModePanel;
    public GameObject autoModePanel;
    public GameObject manualButtonImage;
    public GameObject autoButtonImage;

    [Header("Contenedor de paneles")]
    [Tooltip("El GameObject 'lower' que contiene Articular, Record, Cartesian, TXT")]
    public GameObject lowerContainer;

    public CartesianStateWriterNew  cartesianWriter;
    public JointPositionSubscriber  jointPositionSubscriber;
    public Observador               observador;

    // ------------------------------------------------------------------ //
    void Start()
    {
        manualModePanel.SetActive(true);
        autoModePanel.SetActive(false);
        if (fulcroPanel != null) fulcroPanel.SetActive(false);
        MostrarBotonesprincipales(true);
        UpdateButtonVisuals();
    }

    // ------------------------------------------------------------------ //
    //  Apertura de paneles
    // ------------------------------------------------------------------ //
    public void OpenCartesianPanel() => OpenPanel(cartesianPanel);
    public void OpenJointPanel()     => OpenPanel(jointPanel);
    public void OpenRecordPanel()    => OpenPanel(recordPanel);
    public void OpenTXTPanel()       => OpenPanel(txtPanel);
    public void OpenFulcroPanel()    => OpenPanel(fulcroPanel);

    /// <summary>
    /// Llamar desde el botón VOLVER dentro del panel de Fulcro.
    /// Cierra el panel, muestra los botones principales y reactiva
    /// las actualizaciones de posición articular.
    /// </summary>
    public void CloseFulcroPanel()
    {
        if (fulcroPanel    != null) fulcroPanel.SetActive(false);
        if (lowerContainer != null) lowerContainer.SetActive(true);
        MostrarBotonesprincipales(true);
        if (jointPositionSubscriber != null) jointPositionSubscriber.StartUpdating();
        if (cartesianWriter         != null) cartesianWriter.StartUpdating();
        Debug.Log("[ButtonPanel] Panel Fulcro cerrado.");
    }

    // ------------------------------------------------------------------ //
    //  Modos manual / automático
    // ------------------------------------------------------------------ //
    public void ToggleManualMode()
    {
        bool newState = !manualModePanel.activeSelf;
        manualModePanel.SetActive(newState);
        autoModePanel.SetActive(!newState);
        UpdateButtonVisuals();
    }

    public void ToggleAutoMode()
    {
        bool newState = !autoModePanel.activeSelf;
        autoModePanel.SetActive(newState);
        manualModePanel.SetActive(!newState);
        UpdateButtonVisuals();
    }

    // ------------------------------------------------------------------ //
    //  Lógica central
    // ------------------------------------------------------------------ //
    private void OpenPanel(GameObject panelToOpen)
    {
        // Asegurar que el contenedor esté activo
        bool esFulcro = (panelToOpen == fulcroPanel);
        if (lowerContainer != null) lowerContainer.SetActive(!esFulcro);

        if (cartesianPanel != null) cartesianPanel.SetActive(panelToOpen == cartesianPanel);
        if (jointPanel     != null) jointPanel    .SetActive(panelToOpen == jointPanel);
        if (recordPanel    != null) recordPanel   .SetActive(panelToOpen == recordPanel);
        if (txtPanel       != null) txtPanel      .SetActive(panelToOpen == txtPanel);
        if (fulcroPanel    != null) fulcroPanel   .SetActive(panelToOpen == fulcroPanel);

        MostrarBotonesprincipales(!esFulcro);

        if (panelToOpen == jointPanel  ||
            panelToOpen == recordPanel ||
            panelToOpen == txtPanel)
        {
            if (jointPositionSubscriber != null) jointPositionSubscriber.StartUpdating();
            if (cartesianWriter         != null) cartesianWriter.StartUpdating();
        }
        else if (panelToOpen == cartesianPanel ||
                 panelToOpen == fulcroPanel)
        {
            if (cartesianWriter         != null) cartesianWriter.StopUpdating();
            if (jointPositionSubscriber != null) jointPositionSubscriber.StopUpdating();
        }
    }

    // ------------------------------------------------------------------ //
    //  Mostrar u ocultar los 4 botones del panel principal
    // ------------------------------------------------------------------ //
    private void MostrarBotonesprincipales(bool mostrar)
    {
        if (btnArticular != null) btnArticular.SetActive(mostrar);
        if (btnCartesian != null) btnCartesian.SetActive(mostrar);
        if (btnRecord    != null) btnRecord   .SetActive(mostrar);
        if (btnTxt       != null) btnTxt      .SetActive(mostrar);
    }

    // ------------------------------------------------------------------ //
    //  Botones visuales de modo
    // ------------------------------------------------------------------ //
    private void UpdateButtonVisuals()
    {
        if (manualButtonImage != null && autoButtonImage != null)
        {
            manualButtonImage.SetActive(manualModePanel.activeSelf);
            autoButtonImage  .SetActive(autoModePanel.activeSelf);
        }
    }
}