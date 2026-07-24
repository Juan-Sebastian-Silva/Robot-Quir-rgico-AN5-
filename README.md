# 🤖 Manipulación del robot AN5 al interior de un modelo físico de abdomen humano

<p align="center">
  <img src="docs/images/banner.png" alt="Plataforma RCM AN5" width="800"/>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2022.3_LTS-black?logo=unity" alt="Unity"/>
  <img src="https://img.shields.io/badge/ROS_2-Humble-blue?logo=ros" alt="ROS 2"/>
  <img src="https://img.shields.io/badge/Ubuntu-22.04_LTS-orange?logo=ubuntu" alt="Ubuntu"/>
  <img src="https://img.shields.io/badge/C%23-Scripts-purple?logo=csharp" alt="C#"/>
  <img src="https://img.shields.io/badge/Robot-AN5%20%7C%20FR5-red" alt="AN5"/>
  <img src="https://img.shields.io/badge/Estado-Trabajo_de_grado-green" alt="Estado"/>
</p>

---

## 📋 Descripción

Plataforma de simulación y entrenamiento en robótica quirúrgica mínimamente invasiva, desarrollada como trabajo de grado en el programa de **Ingeniería en Automática Industrial** de la **Universidad del Cauca**.

El proyecto extiende la plataforma base de Garzón y Sarria [[AN5ROS]](#referencias) incorporando:

- ✅ **Restricción de Punto de Fulcro (RCM)** por software en Unity 3D
- ✅ **Gemelo digital bidireccional** del robot AN5 sincronizado en tiempo real
- ✅ **Secuencias de movimiento laparoscópico** programables y ejecutables sobre robot real
- ✅ **Control independiente de J6** para orientación de cámara endoscópica
- ✅ **Visualización de órganos virtuales** del abdomen en el gemelo digital
- ✅ **Comunicación Unity → ROS 2 → Robot AN5** mediante ROSBridge WebSocket

---

## 🏗️ Arquitectura del sistema

```
┌─────────────────────────────────────────────────────────┐
│                      Unity 3D                           │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────────┐ │
│  │ IK_calc  │  │IKController  │  │FulcroCommandSender│ │
│  │ (RCM)    │  │(Sliders XYZ) │  │(Sincronización)   │ │
│  └──────────┘  └──────────────┘  └───────────────────┘ │
│  ┌────────────────────┐  ┌──────────────────────────┐  │
│  │FulcroSequenceManager│  │ JointPositionSubscriber  │  │
│  │(Secuencias)         │  │(Realimentación 10 Hz)    │  │
│  └────────────────────┘  └──────────────────────────┘  │
└──────────────────────┬──────────────────────────────────┘
                       │ WebSocket :9090
                       │ (ROSSharp / ROSBridge)
┌──────────────────────▼──────────────────────────────────┐
│                     ROS 2 Humble                        │
│   /api_command          /current_joint_position         │
│   /current_cartesian_position                           │
└──────────────────────┬──────────────────────────────────┘
                       │ API Fairino (fr_ros2)
┌──────────────────────▼──────────────────────────────────┐
│                   Robot AN5 (FR5)                       │
│              6-DOF · 5 kg payload · ±0.02 mm            │
└─────────────────────────────────────────────────────────┘
```

---

## 🛠️ Requisitos

### Hardware
| Componente | Especificación |
|---|---|
| Robot | AN5 (RobotAnno / FairInnovation FR5) |
| Sistema operativo | Ubuntu 22.04 LTS |
| Procesador | Intel Core i5/i7 o equivalente |
| RAM | 8 GB mínimo (16 GB recomendado) |
| Tarjeta gráfica | Compatible con Unity (OpenGL 4.5+) |

### Software
| Herramienta | Versión |
|---|---|
| Unity | 2022.3 LTS (64-bit Linux) |
| ROS 2 | Humble Hawksbill |
| Python | 3.10+ |
| ROSBridge Suite | ros-humble-rosbridge-suite |
| fr_ros2 (API Fairino) | Compatible con FR5 / AN5 |

---

## 🚀 Instalación

### 1. Clonar el repositorio

```bash
git clone https://github.com/Juan-Sebastian-Silva/nombre-del-repositorio.git
cd nombre-del-repositorio
```

### 2. Instalar dependencias de ROS 2

```bash
# Instalar ROS 2 Humble
sudo apt update && sudo apt install ros-humble-desktop

# Instalar ROSBridge
sudo apt install ros-humble-rosbridge-suite

# Instalar API del robot (fr_ros2)
# Seguir instrucciones del fabricante FairInnovation
```

### 3. Configurar el workspace de ROS 2

```bash
cd ros2_ws
colcon build
source install/setup.bash
```

### 4. Abrir el proyecto en Unity

```
1. Abrir Unity Hub
2. Agregar proyecto desde la carpeta: unity_project/
3. Abrir con Unity 2022.3 LTS
4. Esperar importación de assets y paquetes
```

---

## ▶️ Uso

### Iniciar el sistema

**Terminal 1 — Lanzar ROSBridge:**
```bash
source /opt/ros/humble/setup.bash
ros2 launch rosbridge_server rosbridge_websocket_launch.xml
```

**Terminal 2 — Lanzar nodo del robot AN5:**
```bash
source ~/ros2_ws/install/setup.bash
ros2 run an5_control robot_publisher
```

**Unity:** Dar Play en el editor o ejecutar el build.

### Flujo de operación del modo Fulcro

```
1. Conectar robot AN5 a la red local
2. Iniciar ROSBridge y nodo robot_publisher
3. En Unity: presionar PLAY
4. Activar toggle FULCRO → robot se mueve a pose home
5. Usar sliders XYZ para mover el endoscopio con restricción RCM
6. Guardar puntos con SAVE POINT
7. Ejecutar secuencia con EXECUTE (modo robot real o simulación)
```

---

## 📁 Estructura del repositorio

```
📦 nombre-del-repositorio/
 ┣ 📂 unity_project/
 ┃ ┣ 📂 Assets/
 ┃ ┃ ┣ 📂 Scripts/
 ┃ ┃ ┃ ┣ 📜 IK_calc.cs                  # Motor de cinemática inversa RCM
 ┃ ┃ ┃ ┣ 📜 IKController.cs             # Control de sliders XYZ y J6
 ┃ ┃ ┃ ┣ 📜 FulcroCommandSender.cs      # Sincronización y envío al robot
 ┃ ┃ ┃ ┣ 📜 FulcroSequenceManager.cs    # Gestión de secuencias
 ┃ ┃ ┃ ┣ 📜 JointPositionSubscriber.cs  # Suscriptor de posición real
 ┃ ┃ ┃ ┗ 📜 Ros2CommandSender.cs        # Publicador /api_command
 ┃ ┃ ┣ 📂 Models/
 ┃ ┃ ┃ ┗ 📜 fr5v6.urdf                  # Modelo URDF del robot AN5
 ┃ ┃ ┗ 📂 Scenes/
 ┃ ┗ 📂 Packages/
 ┣ 📂 ros2_ws/
 ┃ ┗ 📂 src/
 ┃   ┣ 📂 an5_control/                  # Nodo ROS 2 del robot AN5
 ┃   ┗ 📂 rosbridge_suite/
 ┣ 📂 sequences/
 ┃ ┗ 📜 fulcro_sequence.txt             # Ejemplo de secuencia guardada
 ┣ 📂 docs/
 ┃ ┣ 📂 images/
 ┃ ┗ 📜 arquitectura.svg
 ┗ 📜 README.md
```

---

## 📐 Parámetros Denavit-Hartenberg (AN5)

| Eslabón | α_{i-1} (rad) | a_{i-1} (m) | d_i (m) | θ_i |
|:---:|:---:|:---:|:---:|:---:|
| 1 | 0 | 0 | 0.152 | θ₁ |
| 2 | π/2 | 0 | 0 | θ₂ |
| 3 | 0 | 0.425 | 0 | θ₃ |
| 4 | 0 | 0.392 | 0.102 | θ₄ |
| 5 | π/2 | 0 | 0.102 | θ₅ |
| 6 | −π/2 | 0 | 0.092 | θ₆ |

---

## 📊 Formato del archivo de secuencia (.txt)

```
fulcro
j1,j2,j3,j4,j5,j6,speed,0,ikX,ikY,ikZ
109.82,-87.22,-89.35,-85.02,63.71,-142.74,10,0,0.651587,0.877000,0.036439
105.98,-97.28,-73.63,-97.23,68.45,-146.21,10,0,0.641588,0.877000,0.036439
```

| Campo | Descripción | Unidad |
|---|---|---|
| j1 – j6 | Ángulos articulares del robot real | grados |
| speed | Velocidad de ejecución MoveJ | % (1–100) |
| 0 | Delay (reservado) | — |
| ikX, ikY, ikZ | Posición del objeto IK en Unity | metros |

---

## 🔬 Resultados

| Métrica | Valor |
|---|---|
| Error medio posición RCM | < 1 mm |
| Frecuencia actualización gemelo digital | 10 Hz |
| Frecuencia recálculo IK_calc | 60 Hz |
| Rango de observación J6 | ~300° |
| Latencia ROSBridge | < 150 ms |

---

## 🎓 Información académica

| | |
|---|---|
| **Institución** | Universidad del Cauca |
| **Facultad** | Ingeniería Electrónica y Telecomunicaciones |
| **Programa** | Ingeniería en Automática Industrial |
| **Autor** | Juan Sebastián Silva |
| **Director** | PhD. Oscar Andrés Vivas Albán |
| **Año** | 2025 |

---

## 📚 Referencias

```
[AN5ROS]  Garzón, A. & Sarria, J. (2025). Plataforma de simulación y manipulación
          del robot AN5 a través de ROS y desde Matlab/Simulink.
          Universidad del Cauca.

[Virtualr] Ruiz, J.D. & Montenegro, J.S. (2023). Plataforma software para la
           manipulación de objetos 3D para un robot UR.
           Universidad del Cauca.

[ColabPR]  Paz, L. & Alvira, J. (2023). Plataforma de Simulación de Trayectorias
           Quirúrgicas en Neurocirugía Utilizando Robots Colaborativos.
           Universidad del Cauca.

[Salgado]  Salgado, J.J. & Vivas, O.A. (2025). UR3e robotic arm manipulation
           with dynamic Remote Motion Center (RCM) for laparoscopic tasks.
           Universidad del Cauca.
```

---

## 📄 Licencia

Este proyecto fue desarrollado con fines académicos en la Universidad del Cauca.
Para uso externo, contactar al autor o al director del proyecto.

---

<p align="center">
  Desarrollado con ❤️ en la Universidad del Cauca · Popayán, Colombia 🇨🇴
</p>
