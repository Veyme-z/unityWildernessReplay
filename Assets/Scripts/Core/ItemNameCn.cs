using UnityEngine;

/// <summary>
/// 物品英文 key → 中文显示名（背包栏 / 交易徽标 / 建造徽标等共用）。
/// 回放数据里的物品名是英文 key（如 copper / Medicine），统一在此做本地化映射。
/// </summary>
public static class ItemNameCn
{
    public static string Cn(string en)
    {
        if (string.IsNullOrEmpty(en)) return en;
        switch (en.ToLowerInvariant())
        {
            case "copper": return "铜";
            case "iron":   return "铁";
            case "stone":  return "石头";
            case "wall":   return "围墙";
            case "tower":  return "防御塔";
            case "medicine": return "药品";
            case "bomb": return "炸弹";
            case "dizzyweapon": return "眩晕法宝";
            case "wallfixer": return "围墙修复包";
            case "smallbeastsummonorder": return "小型野兽召唤令";
            case "middlebeastsummonorder": return "中型野兽召唤令";
            case "largebeastsummonorder": return "大型野兽召唤令";
            case "bossbeastsummonorder": return "首领野兽召唤令";
            case "acienttablet": return "古符石板";
            case "starsand": return "星辰之沙";
            case "flamebreath": return "烈焰之息";
            case "frostpotion": return "寒霜药剂";
            case "thornamulet": return "荆棘护符";
            case "ironwhistle": return "回音铁哨";
            case "upgradestationmaxhp": return "基地耐久强化";
            case "upgradewallmaxhp": return "围墙耐久强化";
            case "upgradetowermaxhp": return "防御塔耐久强化";
            case "upgradetowerattack": return "防御塔攻击强化";
            case "weaponupgradevoucher": return "武器升级道具";
            case "wallupgradevoucher": return "围墙升级道具";
            case "stationupgradevoucher": return "基地升级道具";
            default:       return en;
        }
    }
}
