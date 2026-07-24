using System;
using System.Collections.Generic;
using UnityEngine;
using System.Text;

[ExecuteInEditMode]
public class IK_calc : MonoBehaviour
{
    public Transform ik;
    public Transform rcmPoint;
    
    [Header("Sim Parameters")]
    public static bool mostrarRobotReal = false;

    [Header("Tool Parameters")]
    public double toolLength = 0.30; // 30cm longitud total

    public int solutionID;
    private List<string> IK_Solutions = new List<string>();
    public static List<double> goodSolution = new List<double>();
    public List<Transform> robot = new List<Transform>();

    // Parametros DH del robot UR3e - Modificar para el robot que será usado
    public static double[,] DH_matrix_robot = new double[6, 3] {
            {0f, Mathf.PI / 2f, 0.152f},
            {-0.425f, 0f, 0f},
            {-0.395f, 0f, 0f},
            {0f, Mathf.PI / 2f, 0.102f},
            {0f, -Mathf.PI / 2f, 0.102f},
            {0f, 0f, 0.267f}
        };


    void Update()
    {
        Matrix4x4 tcp_target_matrix;

        if (rcmPoint == null)
        {
            Debug.LogWarning("RCM Point no asignado. Usando IK estándar.");
            tcp_target_matrix = GetTransformMatrix(ik);
        }
        else
        {
            // ---------------------------------------------------------------
            // Cálculo de IK con RCM
            // ---------------------------------------------------------------
            
            // Toma las posiciones de los GameObjects de herramienta (ik) y fulcro(rcm)
            Matrix4x4 T_tip_target = GetTransformMatrix(ik);
            Matrix4x4 T_rcm = GetTransformMatrix(rcmPoint);

            // Extraer las posiciones
            Vector3 P_tip_target = T_tip_target.GetColumn(3);
            Vector3 P_rcm = T_rcm.GetColumn(3);

            // Calcula la dirección de la herramienta
             Vector3 V_tool_direction = (P_tip_target - P_rcm).normalized;

            // Calcula el pivotOffset dinámicamente
            double currentPivotOffset = Vector3.Distance(P_tip_target, P_rcm);

            // Calcula la distancia del TCP al RCM
            double tcpDistance = toolLength - currentPivotOffset;

            // Calcula la posición del TCP
            Vector3 P_tcp_target = P_rcm - (V_tool_direction * (float)tcpDistance);

            // Calcula la orientación del TCP
            Vector3 Z_axis = V_tool_direction;

            // Se usa la orientación del 'ik' (la punta) para definir el "roll"
            // de la herramienta.
            Vector3 X_axis_preferred = T_tip_target.GetColumn(0); // Eje X de la matriz de la punta

            // Se usamos Gram-Schmidt para obtener un eje X ortogonal
            Vector3 X_axis = (X_axis_preferred - Vector3.Project(X_axis_preferred, Z_axis)).normalized;

            // Se obtiene el eje Y
            Vector3 Y_axis = Vector3.Cross(Z_axis, X_axis).normalized;

            // Se construye la matriz de transformación objetivo para el TCP
            tcp_target_matrix = Matrix4x4.identity;
            tcp_target_matrix.SetColumn(0, new Vector4(X_axis.x, X_axis.y, X_axis.z, 0));
            tcp_target_matrix.SetColumn(1, new Vector4(Y_axis.x, Y_axis.y, Y_axis.z, 0));
            tcp_target_matrix.SetColumn(2, new Vector4(Z_axis.x, Z_axis.y, Z_axis.z, 0));
            tcp_target_matrix.SetColumn(3, new Vector4(P_tcp_target.x, P_tcp_target.y, P_tcp_target.z, 1));
        }
        
        // Refleja la matriz a lo largo del eje Y
        Matrix4x4 mt = Matrix4x4.identity;
        mt.m11 = -1;
        Matrix4x4 mt_inverse = mt.inverse;
        Matrix4x4 result = mt * tcp_target_matrix * mt_inverse;

        // Calcula las soluciones de la cinematica inversa
        double[,] solutions = Inverse_kinematic_solutions(result);
        IK_Solutions.Clear();
        IK_Solutions = DisplaySolutions(solutions);

        // Ajusta las articulaciones del robot basados en la solucion seleccionada
        ApplyJointSolution(IK_Solutions, solutions, solutionID, robot);
        goodSolution.Clear();
        goodSolution.Add(solutions[0, 5]);
        goodSolution.Add(solutions[1, 5]);
        goodSolution.Add(solutions[2, 5]);
        goodSolution.Add(solutions[3, 5]);
        goodSolution.Add(solutions[4, 5]);
        goodSolution.Add(solutions[5, 5]);
    }

    // Extrae las matrices de transformacion para la transformacion de entrada 
    // Toma en cuenta que en Unity el sistema de referencia es mano izquierda con eje Y hacia arriba
    public static Matrix4x4 GetTransformMatrix(Transform controller)
    {
        return Matrix4x4.TRS(new Vector3(controller.localPosition.z, controller.localPosition.x, controller.localPosition.y), Quaternion.Euler(controller.localEulerAngles.z, controller.localEulerAngles.x, controller.localEulerAngles.y), new Vector3(1, 1, 1));
    }

    // Calcula la matriz de transformacion usando los parametros DH
    public static Matrix4x4 ComputeTransformMatrix(int jointIndex, double[,] jointAngles)
    {
        jointIndex--;

        // Rotation around the Z-axis
        var rotationZ = Matrix4x4.identity;
        rotationZ.m00 = Mathf.Cos((float)jointAngles[0, jointIndex]);
        rotationZ.m01 = -Mathf.Sin((float)jointAngles[0, jointIndex]);
        rotationZ.m10 = Mathf.Sin((float)jointAngles[0, jointIndex]);
        rotationZ.m11 = Mathf.Cos((float)jointAngles[0, jointIndex]);

        // Translation along the Z-axis
        var translationZ = Matrix4x4.identity;
        translationZ.m23 = (float)DH_matrix_robot[jointIndex, 2];

        // Translation along the X-axis
        var translationX = Matrix4x4.identity;
        translationX.m03 = (float)DH_matrix_robot[jointIndex, 0];

        // Rotation around the X-axis
        var rotationX = Matrix4x4.identity;
        rotationX.m11 = Mathf.Cos((float)DH_matrix_robot[jointIndex, 1]);
        rotationX.m12 = -Mathf.Sin((float)DH_matrix_robot[jointIndex, 1]);
        rotationX.m21 = Mathf.Sin((float)DH_matrix_robot[jointIndex, 1]);
        rotationX.m22 = Mathf.Cos((float)DH_matrix_robot[jointIndex, 1]);

        // Combine the transformations in the following order: rotationZ, translationZ, translationX, and rotationX
        return rotationZ * translationZ * translationX * rotationX;
    }

    // Aplica las soluciones de la cinematica inversa a las articulaciones del robot
    public static void ApplyJointSolution(List<string> solutionStatus, double[,] jointSolutions, int solutionIndex, List<Transform> robotJoints)
    {

        if (solutionStatus[solutionIndex] != "NON DISPONIBLE")
        {
            for (int i = 0; i < robotJoints.Count; i++)
            {
                robotJoints[i].localEulerAngles = ConvertJointAngles(jointSolutions[i, solutionIndex], i);
            }

            float j1 = (float)(Mathf.Rad2Deg * jointSolutions[0, solutionIndex]);
            float j2 = (float)(Mathf.Rad2Deg * jointSolutions[1, solutionIndex]);
            float j3 = (float)(Mathf.Rad2Deg * jointSolutions[2, solutionIndex]);
            float j4 = (float)(Mathf.Rad2Deg * jointSolutions[3, solutionIndex]);
            float j5 = (float)(Mathf.Rad2Deg * jointSolutions[4, solutionIndex]);
            float j6 = (float)(Mathf.Rad2Deg * jointSolutions[5, solutionIndex]);
        }
        else
        {
            Debug.LogError("NO SOLUTION");
        }
    }

    // Convierte los angulos de la articulaciones de radianes a grados y aplica el offset correspondiente. Tiene en cuenta el modelo de Unity
    private static Vector3 ConvertJointAngles(double angleRad, int jointIndex)
    {
        float angleDeg = (float)(Mathf.Rad2Deg * angleRad);
    switch (jointIndex)
    {
       case 0: // J1
            return new Vector3(0, -angleDeg + 22 , 0);

        case 1: // J2
            return new Vector3(-angleDeg + 22 , 0, - 90);

        case 2: // J3
             return new Vector3(0, -angleDeg - 10  , 0);

        case 3: // J4
            return new Vector3(0, -angleDeg - 5 , 0);

        case 4: // J5
            return new Vector3(-angleDeg - 22 , 0, -90);

        case 5: // J6
            return new Vector3(angleDeg, 0, 90);

        default:
            return Vector3.zero;
    }
    }

    // Calula las soluciones de la cinematica inversa
    public static double[,] Inverse_kinematic_solutions(Matrix4x4 transform_matrix_unity)
    {

        double[,] theta = new double[6, 8];

        Vector4 P05 = transform_matrix_unity * new Vector4()
        {
            x = 0,
            y = 0,
            z = -(float)DH_matrix_robot[5, 2],
            w = 1
        }; ;
        float psi = Mathf.Atan2(P05[1], P05[0]);
        float phi = Mathf.Acos((float)((DH_matrix_robot[1, 2] + DH_matrix_robot[3, 2] + DH_matrix_robot[2, 2]) / Mathf.Sqrt(Mathf.Pow(P05[0], 2) + Mathf.Pow(P05[1], 2))));

        theta[0, 0] = psi + phi + Mathf.PI / 2;
        theta[0, 1] = psi + phi + Mathf.PI / 2;
        theta[0, 2] = psi + phi + Mathf.PI / 2;
        theta[0, 3] = psi + phi + Mathf.PI / 2;
        theta[0, 4] = psi - phi + Mathf.PI / 2;
        theta[0, 5] = psi - phi + Mathf.PI / 2;
        theta[0, 6] = psi - phi + Mathf.PI / 2;
        theta[0, 7] = psi - phi + Mathf.PI / 2;

        for (int i = 0; i < 8; i += 4)
        {
            double t5 = (transform_matrix_unity[0, 3] * Mathf.Sin((float)theta[0, i]) - transform_matrix_unity[1, 3] * Mathf.Cos((float)theta[0, i]) - (DH_matrix_robot[1, 2] + DH_matrix_robot[3, 2] + DH_matrix_robot[2, 2])) / DH_matrix_robot[5, 2];
            float th5;
            if (1 >= t5 && t5 >= -1)
            {
                th5 = Mathf.Acos((float)t5);
            }
            else
            {
                th5 = 0;
            }

            if (i == 0)
            {
                theta[4, 0] = th5;
                theta[4, 1] = th5;
                theta[4, 2] = -th5;
                theta[4, 3] = -th5;
            }
            else
            {
                theta[4, 4] = th5;
                theta[4, 5] = th5;
                theta[4, 6] = -th5;
                theta[4, 7] = -th5;
            }
        }

        Matrix4x4 tmu_inverse = transform_matrix_unity.inverse;
        float th0 = Mathf.Atan2((-tmu_inverse[1, 0] * Mathf.Sin((float)theta[0, 0]) + tmu_inverse[1, 1] * Mathf.Cos((float)theta[0, 0])), (tmu_inverse[0, 0] * Mathf.Sin((float)theta[0, 0]) - tmu_inverse[0, 1] * Mathf.Cos((float)theta[0, 0])));
        float th2 = Mathf.Atan2((-tmu_inverse[1, 0] * Mathf.Sin((float)theta[0, 2]) + tmu_inverse[1, 1] * Mathf.Cos((float)theta[0, 2])), (tmu_inverse[0, 0] * Mathf.Sin((float)theta[0, 2]) - tmu_inverse[0, 1] * Mathf.Cos((float)theta[0, 2])));
        float th4 = Mathf.Atan2((-tmu_inverse[1, 0] * Mathf.Sin((float)theta[0, 4]) + tmu_inverse[1, 1] * Mathf.Cos((float)theta[0, 4])), (tmu_inverse[0, 0] * Mathf.Sin((float)theta[0, 4]) - tmu_inverse[0, 1] * Mathf.Cos((float)theta[0, 4])));
        float th6 = Mathf.Atan2((-tmu_inverse[1, 0] * Mathf.Sin((float)theta[0, 6]) + tmu_inverse[1, 1] * Mathf.Cos((float)theta[0, 6])), (tmu_inverse[0, 0] * Mathf.Sin((float)theta[0, 6]) - tmu_inverse[0, 1] * Mathf.Cos((float)theta[0, 6])));

        theta[5, 0] = th0;
        theta[5, 1] = th0;
        theta[5, 2] = th2;
        theta[5, 3] = th2;
        theta[5, 4] = th4;
        theta[5, 5] = th4;
        theta[5, 6] = th6;
        theta[5, 7] = th6;

        for (int i = 0; i <= 7; i += 2)
        {
            double[,] t1 = new double[1, 6];
            t1[0, 0] = theta[0, i];
            t1[0, 1] = theta[1, i];
            t1[0, 2] = theta[2, i];
            t1[0, 3] = theta[3, i];
            t1[0, 4] = theta[4, i];
            t1[0, 5] = theta[5, i];
            Matrix4x4 T01 = ComputeTransformMatrix(1, t1);
            Matrix4x4 T45 = ComputeTransformMatrix(5, t1);
            Matrix4x4 T56 = ComputeTransformMatrix(6, t1);
            Matrix4x4 T14 = T01.inverse * transform_matrix_unity * (T45 * T56).inverse;

            Vector4 P13 = T14 * new Vector4()
            {
                x = 0,
                y = (float)-DH_matrix_robot[3, 2],
                z = 0,
                w = 1
            };
            double t3 = (Mathf.Pow(P13[0], 2) + Mathf.Pow(P13[1], 2) - Mathf.Pow((float)DH_matrix_robot[1, 0], 2) - Mathf.Pow((float)DH_matrix_robot[2, 0], 2)) / (2 * DH_matrix_robot[1, 0] * DH_matrix_robot[2, 0]);
            double th3;
            if (1 >= t3 && t3 >= -1)
            {
                th3 = Mathf.Acos((float)t3);
            }
            else
            {
                th3 = 0;
            }
            theta[2, i] = th3;
            theta[2, i + 1] = -th3;
        }

        for (int i = 0; i < 8; i++)
        {
            double[,] t1 = new double[1, 6];
            t1[0, 0] = theta[0, i];
            t1[0, 1] = theta[1, i];
            t1[0, 2] = theta[2, i];
            t1[0, 3] = theta[3, i];
            t1[0, 4] = theta[4, i];
            t1[0, 5] = theta[5, i];
            Matrix4x4 T01 = ComputeTransformMatrix(1, t1);
            Matrix4x4 T45 = ComputeTransformMatrix(5, t1);
            Matrix4x4 T56 = ComputeTransformMatrix(6, t1);
            Matrix4x4 T14 = T01.inverse * transform_matrix_unity * (T45 * T56).inverse;

            Vector4 P13 = T14 * new Vector4()
            {
                x = 0,
                y = (float)-DH_matrix_robot[3, 2],
                z = 0,
                w = 1
            };

            theta[1, i] = Mathf.Atan2(-P13[1], -P13[0]) - Mathf.Asin((float)(-DH_matrix_robot[2, 0] * Mathf.Sin((float)theta[2, i]) / Mathf.Sqrt(Mathf.Pow(P13[0], 2) + Mathf.Pow(P13[1], 2))));

            double[,] t2 = new double[1, 6];
            t2[0, 0] = theta[0, i];
            t2[0, 1] = theta[1, i];
            t2[0, 2] = theta[2, i];
            t2[0, 3] = theta[3, i];
            t2[0, 4] = theta[4, i];
            t2[0, 5] = theta[5, i];
            Matrix4x4 T32 = ComputeTransformMatrix(3, t2).inverse;
            Matrix4x4 T21 = ComputeTransformMatrix(2, t2).inverse;
            Matrix4x4 T34 = T32 * T21 * T14;
            theta[3, i] = Mathf.Atan2(T34[1, 0], T34[0, 0]);
        }
        return theta;
    }

    public static List<string> DisplaySolutions(double[,] solutions)
    {
        List<string> info = new List<string>();

        // Iteraa través de 8 posibles soluciones
        for (int column = 0; column < 8; column++)
        {
            // Revisa si todos los angulos articulares en la solucion son validos (no NaN)
            bool isValidSolution = true;
            for (int row = 0; row < 6; row++)
            {
                if (double.IsNaN(solutions[row, column]))
                {
                    isValidSolution = false;
                    break;
                }
            }

            // Si la solucion es valida, da formato y agrega los angulos articulares a la lista
            if (isValidSolution)
            {
                string solutionInfo = "";
                for (int row = 0; row < 6; row++)
                {
                    double angleInDegrees = Math.Round(Mathf.Rad2Deg * solutions[row, column], 2);
                    solutionInfo += $"{angleInDegrees}";

                    if (row < 5)
                    {
                        solutionInfo += " | ";
                    }
                }
                info.Add(solutionInfo);
            }
            // Si la solucion no es valida, agrega "NON DISPONIBLE" a la lista
            else
            {
                info.Add("NON DISPONIBLE");
            }
        }

        return info;
    }

}
