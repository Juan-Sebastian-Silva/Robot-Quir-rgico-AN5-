/*******************
Autores:    Angel Garzon Sarzosa (ahgarzon@unicauca.edu.co)
            Jhoan Simei Sarria (simei@unicauca.edu.co)                   
*******************/
using UnityEngine;
using UnityEngine.UI;

public class Observador : MonoBehaviour
{
    [Header("Cámaras virtuales")]
    public Camera camara1;
    public Camera camara2;
    public Camera camara3;
    public Camera camara4;

    [Header("Cámara real")]
    public Camera camara5;
    public RawImage camara5Display; // RawImage donde se mostrará la cámara real

    [Header("Parámetros de movimiento")]
    public float VelMov = 5f;
    public float VelRot = 60f;

    private Vector3 Movimiento;
    private Vector2 Rotacion;
    private Vector3 PosicionInicial;
    private Quaternion RotacionInicial;

    private bool camarasExtraActivas = false;

    // Control de cámara real (webcam)
    private WebCamTexture webcamTexture;
    private bool camaraRealActiva = false;

    void Awake()
    {
        PosicionInicial = transform.position;
        RotacionInicial = transform.rotation;
    }

    void Start()
    {
        MostrarSoloCamara(camara1);

        if (camara4 != null) camara4.enabled = false;
        if (camara5 != null) camara5.enabled = false;
        if (camara5Display != null) camara5Display.gameObject.SetActive(false);
    }

    void Update()
    {
        Movimiento.x = Input.GetAxis("Horizontal") * VelMov * Time.deltaTime;
        Movimiento.y = Input.GetAxis("Vertical") * VelMov * Time.deltaTime;
        Movimiento.z = Input.GetAxis("Altura") * VelMov * Time.deltaTime;
        Rotacion.x = Input.GetAxis("RotHorizontal") * VelRot * Time.deltaTime;

        MoverVista();

        if (Input.GetKeyDown(KeyCode.R))
            ReiniciarVista();

        if (Input.GetKeyDown(KeyCode.Alpha1)) MostrarSoloCamara(camara1);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) MostrarSoloCamara(camara2);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) MostrarSoloCamara(camara3);
    }

    void MoverVista()
    {
        transform.Translate(Movimiento.x, Movimiento.z, Movimiento.y);
        transform.Rotate(0, Rotacion.x, 0);
    }

    void ReiniciarVista()
    {
        transform.position = PosicionInicial;
        transform.rotation = RotacionInicial;
    }

    void MostrarSoloCamara(Camera camaraActiva)
    {
        camara1.enabled = false;
        camara2.enabled = false;
        camara3.enabled = false;

        if (camaraActiva != null)
            camaraActiva.enabled = true;
    }

    // =======================================
    // CONTROL DESDE BOTÓN UI
    // =======================================
    public void ActivarCamarasExtraDesdeUI()
    {
        if (!camarasExtraActivas)
        {
            ActivarCamarasExtra();
            camarasExtraActivas = true;
            Debug.Log("[Observador] Cámaras extra ACTIVADAS.");
        }
        else
        {
            DesactivarCamarasExtra();
            camarasExtraActivas = false;
            Debug.Log("[Observador] Cámaras extra DESACTIVADAS.");
        }
    }

    private void ActivarCamarasExtra()
    {
        // Cámara 4
        if (camara4 == null)
        {
            GameObject cam4Obj = new GameObject("Camara4");
            camara4 = cam4Obj.AddComponent<Camera>();
            camara4.rect = new Rect(0.75f, 0f, 0.25f, 0.25f);
            camara4.depth = 2;
            camara4.clearFlags = CameraClearFlags.Depth;
        }
        camara4.enabled = true;

        // Cámara 5 (real)
        if (camara5Display != null)
        {
            ActivarCamaraReal();
        }
        else
        {
            // Si no hay RawImage, activa solo la cámara 5 virtual
            if (camara5 == null)
            {
                GameObject cam5Obj = new GameObject("Camara5");
                camara5 = cam5Obj.AddComponent<Camera>();
                camara5.rect = new Rect(0.5f, 0.5f, 0.5f, 0.5f);
                camara5.depth = 3;
                camara5.clearFlags = CameraClearFlags.Depth;
            }
            camara5.enabled = true;
        }
    }

    private void DesactivarCamarasExtra()
    {
        if (camara4 != null) camara4.enabled = false;

        if (camara5Display != null && camara5Display.gameObject.activeSelf)
            DesactivarCamaraReal();

        if (camara5 != null)
            camara5.enabled = false;
    }

    // =======================================
    // CONTROL DE CÁMARA REAL
    // =======================================
    private void ActivarCamaraReal()
{
    WebCamDevice[] devices = WebCamTexture.devices;

    if (devices.Length == 0)
    {
        Debug.LogWarning("⚠️ No se encontró ninguna cámara conectada.");
        return;
    }

    // Usar /dev/video0 (la cámara principal)
    string camName = devices[0].name;
    Debug.Log("🎥 Activando cámara: " + camName);

    webcamTexture = new WebCamTexture(camName, 320, 240, 15);
    camara5Display.texture = webcamTexture;
    camara5Display.gameObject.SetActive(true);

    webcamTexture.Play();
    camaraRealActiva = true;
}


    private void DesactivarCamaraReal()
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Stop();

        camara5Display.texture = null;
        camara5Display.gameObject.SetActive(false);
        camaraRealActiva = false;

        Debug.Log("📴 Cámara real desactivada");
    }
}
