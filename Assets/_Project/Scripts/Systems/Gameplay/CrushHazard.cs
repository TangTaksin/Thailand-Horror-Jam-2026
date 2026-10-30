using UnityEngine;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Component attached to objects that can crush the player (e.g. falling heavy boxes,
    /// descending crushers, hydraulic ceilings, moving platforms).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrushHazard : MonoBehaviour
    {
        [Header("Crush Settings")]
        [Tooltip("ความเร็วตกขั้นต่ำ (m/s) ของวัตถุที่กระแทกศีรษะแล้วทำให้ตายทันที")]
        [SerializeField] private float _lethalImpactSpeed = 2.0f;

        [Tooltip("สามารถทำให้ผู้เล่นตายจากแรงกระแทกเมื่อร่วงตกใส่ศีรษะได้หรือไม่")]
        [SerializeField] private bool _canCrushFalling = true;

        [Tooltip("สามารถบดอัดผู้เล่นในแนวดิ่ง (ระหว่างพื้นกับเพดาน) ได้หรือไม่")]
        [SerializeField] private bool _canCrushSqueeze = true;

        public float LethalImpactSpeed => _lethalImpactSpeed;
        public bool CanCrushFalling => _canCrushFalling;
        public bool CanCrushSqueeze => _canCrushSqueeze;

        /// <summary>
        /// ตรวจสอบว่า GameObject หรือ Collider ใดๆ ถือเป็น Crush Hazard หรือไม่
        /// (ตรวจทั้ง Component CrushHazard หรือ Tag 'Hazard' / 'CrushHazard')
        /// </summary>
        public static bool IsCrushHazard(GameObject target, out CrushHazard hazardComp)
        {
            if (target == null)
            {
                hazardComp = null;
                return false;
            }

            hazardComp = target.GetComponentInParent<CrushHazard>();
            if (hazardComp != null)
            {
                return true;
            }

            if (target.CompareTag("Hazard") || target.CompareTag("CrushHazard"))
            {
                return true;
            }

            Transform parent = target.transform.parent;
            while (parent != null)
            {
                if (parent.CompareTag("Hazard") || parent.CompareTag("CrushHazard"))
                {
                    return true;
                }
                parent = parent.parent;
            }

            return false;
        }
    }
}
