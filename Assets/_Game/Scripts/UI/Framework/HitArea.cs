using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Invisible raycast target (draws nothing, costs no fill-rate). Used for stable button hit areas.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HitArea : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
