using UnityEngine;

namespace MultiplayerARPG
{
    public static class CraftingQueueSourceExtensions
    {
        public static bool IsInCraftDistance(this ICraftingQueueSource source, Vector3 crafterPosition)
        {
            return source.CraftingDistance <= 0f || Vector3.Distance(crafterPosition, source.transform.position) <= source.CraftingDistance;
        }

        public static void UpdateQueue(this ICraftingQueueSource source)
        {
            if (!source.CanCraft)
            {
                if (source.QueueItems.Count > 0)
                    source.QueueItems.Clear();
                return;
            }

            if (source.QueueItems.Count == 0)
            {
                source.TimeCounter = 0f;
                return;
            }

            CraftingQueueItem craftingItem = source.QueueItems[0];
            ItemCraftFormula formula = GameInstance.ItemCraftFormulas[craftingItem.dataId];
            if (!BaseGameNetworkManager.Singleton.TryGetEntityByObjectId(craftingItem.crafterId, out BasePlayerCharacterEntity crafter))
            {
                source.QueueItems.RemoveAt(0);
                return;
            }

            BaseGameEntity sourceEntity;
            if (!BaseGameNetworkManager.Singleton.TryGetEntityByObjectId(craftingItem.sourceObjectId, out sourceEntity))
            {
                source.QueueItems.RemoveAt(0);
                return;
            }

            if (!source.IsInCraftDistance(crafter.EntityTransform.position))
            {
                source.QueueItems.RemoveAt(0);
                return;
            }

            if (!formula.ItemCraft.CanCraft(crafter, out UITextKeys errorMessage))
            {
                source.TimeCounter = 0f;
                source.QueueItems.RemoveAt(0);
                GameInstance.ServerGameMessageHandlers.SendGameMessage(crafter.ConnectionId, errorMessage);
                return;
            }

            source.TimeCounter += Time.unscaledDeltaTime;
            if (source.TimeCounter >= 1f)
            {
                craftingItem.craftRemainsDuration -= source.TimeCounter;
                source.TimeCounter = 0f;

                if (craftingItem.craftRemainsDuration <= 0f)
                {
                    string ingredientsHash = formula.ItemCraft.GenerateIngredientsHash(crafter);
                    string storageKey = "crafting";
                    string actionKey = $"{formula.DataId}:{ingredientsHash}";

                    if (ItemTransactionGuard.BeginTransaction(crafter.Id, storageKey, actionKey))
                    {
                        try
                        {
                            // Final server-side validation
                            if (!formula.ItemCraft.CanCraft(crafter, out UITextKeys finalError))
                            {
                                GameInstance.ServerGameMessageHandlers.SendGameMessage(crafter.ConnectionId, finalError);
                                return;
                            }

                            if (crafter.IncreasingItemsWillOverwhelming(
                                new ItemAmount[] { new ItemAmount { item = formula.ItemCraft.CraftingItem, amount = 1 } }))
                            {
                                GameInstance.ServerGameMessageHandlers.SendGameMessage(crafter.ConnectionId, UITextKeys.UI_ERROR_WILL_OVERWHELMING);
                                return;
                            }

                            // Remove required materials & currencies
                            formula.ItemCraft.ConsumeMaterials(crafter);

                            // Grant crafted item to non-equip inventory
                            crafter.NonEquipItems.IncreaseItems(
                                new ItemAmount[] { new ItemAmount { item = formula.ItemCraft.CraftingItem, amount = 1 } });
                        }
                        finally
                        {
                            ItemTransactionGuard.EndTransaction(crafter.Id, storageKey, actionKey);
                        }
                    }

                    // Adjust or remove queue item
                    if (craftingItem.amount > 1)
                    {
                        --craftingItem.amount;
                        craftingItem.craftRemainsDuration = formula.CraftDuration;
                        source.QueueItems[0] = craftingItem;
                    }
                    else
                    {
                        source.QueueItems.RemoveAt(0);
                    }
                }
                else
                {
                    source.QueueItems[0] = craftingItem;
                }
            }
        }

        public static bool AppendCraftingQueueItem(this ICraftingQueueSource source, BasePlayerCharacterEntity crafter, int dataId, int amount, out UITextKeys errorMessage)
        {
            if (source.ObjectId != crafter.ObjectId && !source.PublicQueue)
            {
                return crafter.Crafting.AppendCraftingQueueItem(crafter, dataId, amount, out errorMessage);
            }
            errorMessage = UITextKeys.NONE;
            if (!source.CanCraft)
                return false;
            ItemCraftFormula itemCraftFormula;
            if (!GameInstance.ItemCraftFormulas.TryGetValue(dataId, out itemCraftFormula))
                return false;
            if (!itemCraftFormula.ItemCraft.CanCraft(crafter, out errorMessage))
                return false;
            if (source.QueueItems.Count >= source.MaxQueueSize)
                return false;
            source.QueueItems.Add(new CraftingQueueItem()
            {
                crafterId = crafter.ObjectId,
                dataId = dataId,
                amount = amount,
                craftRemainsDuration = itemCraftFormula.CraftDuration,
                sourceObjectId = source.ObjectId,
            });
            return true;
        }

        public static void ChangeCraftingQueueItem(this ICraftingQueueSource source, uint crafterId, int index, int amount)
        {
            if (!source.CanCraft)
                return;
            if (index < 0 || index >= source.QueueItems.Count)
                return;
            if (source.QueueItems[index].crafterId != crafterId)
                return;
            if (amount <= 0)
            {
                source.QueueItems.RemoveAt(index);
                return;
            }
            CraftingQueueItem craftingItem = source.QueueItems[index];
            craftingItem.amount = amount;
            source.QueueItems[index] = craftingItem;
        }

        public static void CancelCraftingQueueItem(this ICraftingQueueSource source, uint crafterId, int index)
        {
            if (!source.CanCraft)
                return;
            if (index < 0 || index >= source.QueueItems.Count)
                return;
            if (source.QueueItems[index].crafterId != crafterId)
                return;
            source.QueueItems.RemoveAt(index);
        }
    }
}
