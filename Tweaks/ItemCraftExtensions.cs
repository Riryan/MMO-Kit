using UnityEngine;
using System.Collections.Generic; 

namespace MultiplayerARPG
{
    public static class ItemCraftExtensions
    {
        public static string GenerateIngredientsHash(this ItemCraft craft, IPlayerCharacterData player)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (var req in craft.RequireItems)
                sb.Append(req.item.DataId).Append("x").Append(req.amount).Append("|");
            foreach (var req in craft.RequireCurrencies)
                sb.Append(req.currency.DataId).Append("x").Append(req.amount).Append("|");
            return sb.ToString().GetHashCode().ToString("X");
        }

        public static void ConsumeMaterials(this ItemCraft craft, BasePlayerCharacterEntity player)
        {
            foreach (var req in craft.RequireItems)
                player.DecreaseItems(req.item.DataId, req.amount);

            if (craft.RequireCurrencies != null && craft.RequireCurrencies.Length > 0)
            {
                var currencyDict = new Dictionary<Currency, int>();
                foreach (var req in craft.RequireCurrencies)
                    currencyDict[req.currency] = req.amount;

                player.DecreaseCurrencies(currencyDict);
            }
        }
    }
}