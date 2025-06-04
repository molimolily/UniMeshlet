using UnityEngine;

namespace UniMeshlet.Runtime.Util
{
    public static class FrustumUtil
    {
        /// <summary>VP 行列から 6 平面を world-space で取り出す</summary>
        /// <param name="vp">view * projection またはそれをさらに改変した行列</param>
        /// <param name="planes">結果を書き込む Vector4[6] (xyz = 法線, w = オフセット)</param>
        /// <param name="useReversedZ">リバースZ</param>
        public static void ExtractPlanes(Matrix4x4 vp, Vector4[] planes, bool useReversedZ)
        {
            // 行(ROW) を取り出す
            Vector4 r0 = new Vector4(vp.m00, vp.m01, vp.m02, vp.m03);
            Vector4 r1 = new Vector4(vp.m10, vp.m11, vp.m12, vp.m13);
            Vector4 r2 = new Vector4(vp.m20, vp.m21, vp.m22, vp.m23);
            Vector4 r3 = new Vector4(vp.m30, vp.m31, vp.m32, vp.m33);
        
            planes[0] = NormalizePlane(r3 + r0);   // Left
            planes[1] = NormalizePlane(r3 - r0);   // Right
            planes[2] = NormalizePlane(r3 + r1);   // Bottom
            planes[3] = NormalizePlane(r3 - r1);   // Top
            planes[4] = useReversedZ ? NormalizePlane(r3 - r2) : NormalizePlane(r3 + r2);   // Near
            planes[5] = useReversedZ ? NormalizePlane(r3 + r2) : NormalizePlane(r3 - r2);   // Far
        }

        static Vector4 NormalizePlane(Vector4 p)
        {
            float invLen = 1.0f /
                           Mathf.Sqrt(p.x * p.x + p.y * p.y + p.z * p.z);
            return p * invLen;
        }
    }
}