using UnityEngine;
using UnityEngine.UI;

public class CamaraWebController : MonoBehaviour
{
    [Header("RawImage donde se mostrará la cámara real")]
    public RawImage camara5Display;

    private WebCamTexture webcamTexture;
    private bool camaraActiva = false;

    void Start()
    {
        if (camara5Display != null)
            camara5Display.gameObject.SetActive(false); // Oculta la RawImage al iniciar
    }

    // 🔘 Llamar este método desde un botón para activar/desactivar la cámara real
    public void ToggleCamaraReal()
    {
        if (!camaraActiva)
        {
            ActivarCamara();
        }
        else
        {
            DesactivarCamara();
        }
    }

    private void ActivarCamara()
    {
        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices.Length == 0)
        {
            Debug.LogWarning("⚠️ No se encontró ninguna cámara conectada.");
            return;
        }

        try
        {
            webcamTexture = new WebCamTexture(devices[0].name, 640, 480, 30);
            camara5Display.texture = webcamTexture;
            camara5Display.gameObject.SetActive(true);
            webcamTexture.Play();

            camaraActiva = true;
            Debug.Log("✅ Cámara real activada: " + devices[0].name);
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ Error al iniciar la cámara: " + e.Message);
        }
    }

    private void DesactivarCamara()
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
        }

        if (camara5Display != null)
        {
            camara5Display.texture = null;
            camara5Display.gameObject.SetActive(false);
        }

        camaraActiva = false;
        Debug.Log("📴 Cámara real desactivada");
    } 
}
