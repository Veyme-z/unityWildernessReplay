using UnityEngine;

/// <summary>billboard：始终面向主相机（2D 立牌 / 血条用）</summary>
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.rotation = cam.transform.rotation;
    }
}
