using UnityEngine;

/// Preview scene only (not in the bundle): makes the camera render the depth texture the pool water shader reads (Valheim's camera does that itself).
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class DTP_DepthOn : MonoBehaviour
{
    void OnEnable() { GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth; }
    void Update() { GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth; }
}
