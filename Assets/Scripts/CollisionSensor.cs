using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CollisionSensor : MonoBehaviour
{
    [Header("충돌 감지 플래그 (MainControl에서 읽음)")]
    public bool p1 = false;
    public bool p2 = false;
    public bool p3 = false;
    public bool p4 = false;

    [Header("컬러 센서 (양품=true, 불량품=false)")]
    public bool n1;
    public bool n2;
    public bool n3;

    private void OnCollisionEnter(Collision other)
    {
        if (!other.gameObject.CompareTag("TargetObject")) return;

        Debug.Log("[CollisionSensor] OnCollisionEnter from " + gameObject.name + ", hit: " + other.gameObject.name);

        MeshRenderer r = other.gameObject.GetComponent<MeshRenderer>();
        bool isNormal = true;
        if (r != null)
        {
            Color c = r.material.color;
            isNormal = !(c.r > 0.5f && c.g < 0.3f && c.b < 0.3f);
        }

        switch (gameObject.name)
        {
            case "Sensor1":
                p1 = true;
                n1 = isNormal;
                Debug.Log("[Sensor1] 부품 감지! 양품=" + n1);
                break;
            case "Sensor2":
                p2 = true;
                n2 = isNormal;
                Debug.Log("[Sensor2] 부품 감지! 양품=" + n2);
                break;
            case "Sensor3":
                p3 = true;
                n3 = isNormal;
                Debug.Log("[Sensor3] 부품 감지! 양품=" + n3);
                break;
            case "Sensor4":
                p4 = true;
                Debug.Log("[Sensor4] 부품 감지!");
                break;
        }
    }

    private void OnCollisionExit(Collision other)
    {
        if (!other.gameObject.CompareTag("TargetObject")) return;

        switch (gameObject.name)
        {
            case "Sensor1": p1 = false; n1 = false; break;
            case "Sensor2": p2 = false; n2 = false; break;
            case "Sensor3": p3 = false; n3 = false; break;
            case "Sensor4": p4 = false; break;
        }
    }
}