using UnityEngine;

namespace MyGame.Foundation
{
    public sealed class SupplyPickup : MonoBehaviour
    {
        public ItemStack Stack { get; private set; }
        private bool claimed;
        public Material OwnedMaterial { get; set; }
        public void Initialize(ItemStack stack) { Stack = stack; }
        private void OnDestroy()
        {
            if (OwnedMaterial != null) Destroy(OwnedMaterial);
        }
        public bool TryCollect(Inventory inventory)
        {
            if (claimed || Stack == null || !inventory.TryAdd(Stack.Item, Stack.Count)) return false;
            claimed = true;
            // Deactivate immediately: Destroy is deferred and must not allow duplicate pickup.
            gameObject.SetActive(false); Destroy(gameObject); return true;
        }
    }
}
