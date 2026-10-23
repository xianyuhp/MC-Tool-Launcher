using System.Text;

namespace MCTL.Core.Commands;

/// <summary>指令语法版本区间（按原版语法分界点划分）。</summary>
public enum CommandVersion
{
    /// <summary>1.12 及更早：数字 ID + 旧 NBT。</summary>
    V1_12 = 0,
    /// <summary>1.13 – 1.20.4：扁平化 ID + NBT。</summary>
    V1_13To1_20_4 = 1,
    /// <summary>1.20.5 – 1.21.4：数据组件（方括号），装备仍用 HandItems / ArmorItems。</summary>
    V1_20_5To1_21_4 = 2,
    /// <summary>1.21.5+：装备合并到 equipment，文本组件倾向 SNBT。</summary>
    V1_21_5Plus = 3
}

public static class CommandVersionExtensions
{
    public static readonly (CommandVersion Version, string Label, string Hint)[] All =
    {
        (CommandVersion.V1_12, "1.12 及更早", "旧版：物品数据用 {NBT}，附魔 Enchantments:[{id,lvl}]，名称为 display.Name。"),
        (CommandVersion.V1_13To1_20_4, "1.13 – 1.20.4", "物品使用 {NBT}：附魔 Enchantments:[{id:\"minecraft:sharpness\",lvl:5}]，名称 display.Name。"),
        (CommandVersion.V1_20_5To1_21_4, "1.20.5 – 1.21.4", "1.20.5+ 物品改用数据组件 []，旧 NBT 写法会直接报错；装备仍用 HandItems / ArmorItems。"),
        (CommandVersion.V1_21_5Plus, "1.21.5+", "1.21.5+ 装备合并到 equipment 组件，文本组件可直接使用 SNBT 字符串。")
    };

    /// <summary>1.20.5+ 使用数据组件（[]）。</summary>
    public static bool UsesComponents(this CommandVersion version) => version >= CommandVersion.V1_20_5To1_21_4;

    /// <summary>1.21.5+ 使用 equipment 组件。</summary>
    public static bool UsesEquipmentComponent(this CommandVersion version) => version == CommandVersion.V1_21_5Plus;

    /// <summary>1.21.5+ 文本组件更倾向 SNBT 字符串。</summary>
    public static bool UsesSnbtText(this CommandVersion version) => version == CommandVersion.V1_21_5Plus;

    /// <summary>1.13+ 扁平化 ID（mob 仍为 minecraft:xxx）。</summary>
    public static bool UsesNamespacedIds(this CommandVersion version) => version >= CommandVersion.V1_13To1_20_4;
}

public record EnchantEntry(string Id, int Level);

/// <summary>属性条目：例如盔甲值 generic.armor = 20。</summary>
public record AttributeEntry(string Id, double Amount);

public class GiveOptions
{
    public string Target { get; set; } = "@p";
    public string ItemId { get; set; } = "minecraft:diamond_sword";
    public int Count { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Lore { get; set; } = "";
    public bool Unbreakable { get; set; }
    public List<EnchantEntry> Enchantments { get; set; } = new();
    /// <summary>物品属性修饰符（AttributeModifiers / attribute_modifiers）。</summary>
    public List<AttributeEntry> Attributes { get; set; } = new();
}

public class SummonOptions
{
    public string Target { get; set; } = "~ ~ ~";
    public string EntityId { get; set; } = "minecraft:zombie";
    public string Name { get; set; } = "";
    public double? Health { get; set; }
    public bool Persistence { get; set; } = true;
    public string MainHand { get; set; } = "";
    public string OffHand { get; set; } = "";
    public string Head { get; set; } = "";
    public string Chest { get; set; } = "";
    public string Legs { get; set; } = "";
    public string Feet { get; set; } = "";
    /// <summary>实体基础属性（盔甲值、攻击力等）。</summary>
    public List<AttributeEntry> Attributes { get; set; } = new();
}

/// <summary>指令语法适配层：集中处理版本分界，避免散落 if-else。</summary>
public static class CommandSyntax
{
    public static string ComponentHint(CommandVersion version) => version switch
    {
        CommandVersion.V1_12 => "1.12 使用数字 ID 与旧 NBT，附魔与名称写在 { } 中。",
        CommandVersion.V1_13To1_20_4 => "1.13–1.20.4 使用扁平化 ID + {NBT}，附魔 Enchantments:[{id,lvl}]。",
        CommandVersion.V1_20_5To1_21_4 => "1.20.5+ 使用数据组件 [ ]，旧 {NBT} 写法会直接报错。",
        _ => "1.21.5+ 装备使用 equipment 组件，文本组件可直接写 SNBT 字符串。"
    };


    public static string BuildGive(CommandVersion version, GiveOptions options)
    {
        var id = NormalizeItem(options.ItemId, version);
        var count = Math.Clamp(options.Count, 1, 6400);

        if (version == CommandVersion.V1_12)
        {
            var nbt = BuildItemNbt(version, options);
            var nbtPart = nbt.Length > 0 ? " {" + nbt + "}" : "";
            // 1.12：/give <玩家> <物品> [数量] [数据值] [NBT]
            return $"/give {options.Target} {id} {count} 0{nbtPart}";
        }

        if (!version.UsesComponents())
        {
            var nbt = BuildItemNbt(version, options);
            var nbtPart = nbt.Length > 0 ? "{" + nbt + "}" : "";
            return $"/give {options.Target} {id}{nbtPart} {count}";
        }

        var components = BuildComponents(version, options);
        var componentPart = components.Count > 0 ? "[" + string.Join(",", components) + "]" : "";
        return $"/give {options.Target} {id}{componentPart} {count}";
    }

    private static string BuildItemNbt(CommandVersion version, GiveOptions options)
    {
        var parts = new List<string>();

        var display = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Name))
            display.Add("Name:" + JsonNbtText(version, options.Name));
        if (!string.IsNullOrWhiteSpace(options.Lore))
        {
            var lines = options.Lore
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNbtText(version, line.Trim()));
            display.Add("Lore:[" + string.Join(",", lines) + "]");
        }
        if (display.Count > 0) parts.Add("display:{" + string.Join(",", display) + "}");

        if (options.Enchantments.Count > 0)
        {
            var suffix = version == CommandVersion.V1_12 ? "s" : "";
            parts.Add("Enchantments:[" + string.Join(",", options.Enchantments
                .Where(e => e.Level > 0)
                .Select(e =>
                {
                    // 1.12 的附魔 ID 不带命名空间
                    var name = NormalizeEnchant(e.Id);
                    if (version == CommandVersion.V1_12) name = name.Replace("minecraft:", "");
                    return $"{{id:\"{name}\",lvl:{e.Level}{suffix}}}";
                })) + "]");
        }

        if (options.Unbreakable) parts.Add("Unbreakable:1b");
        if (options.Attributes.Count > 0)
        {
            var attributes = BuildItemAttributes(version, options.Attributes);
            if (attributes.Length > 0) parts.Add(attributes);
        }
        return string.Join(",", parts);
    }

    private static List<string> BuildComponents(CommandVersion version, GiveOptions options)
    {
        var components = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Name))
            components.Add("custom_name=" + TextComponentArgument(version, options.Name));

        if (!string.IsNullOrWhiteSpace(options.Lore))
        {
            var lines = options.Lore
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => TextComponentArgument(version, line.Trim()));
            components.Add("lore=[" + string.Join(",", lines) + "]");
        }

        var enchants = options.Enchantments.Where(e => e.Level > 0).ToList();
        if (enchants.Count > 0)
            components.Add("enchantments={levels:{" +
                           string.Join(",", enchants.Select(e =>
                               $"{NormalizeEnchant(e.Id).Replace("minecraft:", "")}:{e.Level}")) + "}}");

        if (options.Unbreakable) components.Add("unbreakable={}");
        if (options.Attributes.Count > 0)
        {
            var attributes = BuildItemAttributes(version, options.Attributes);
            if (attributes.Length > 0) components.Add(attributes);
        }
        return components;
    }


    public static string BuildSummon(CommandVersion version, SummonOptions options)
    {
        var id = NormalizeEntity(options.EntityId, version);
        var nbt = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Name))
            nbt.Add("CustomName:" + TextComponentNbt(version, options.Name));

        if (options.Health is > 0)
        {
            nbt.Add($"Health:{(float)options.Health.Value:0.##}f");
        }

        if (options.Persistence) nbt.Add("PersistenceRequired:1b");

        // 生命值本身也是属性，与用户自定义的盔甲值等合并到一个属性表里
        var attributes = new List<AttributeEntry>();
        if (options.Health is > 0) attributes.Add(new AttributeEntry("generic.max_health", options.Health.Value));
        attributes.AddRange(options.Attributes);
        if (attributes.Count > 0) nbt.Add(BuildEntityAttributes(version, attributes));

        var equipment = BuildEquipment(version, options);
        nbt.AddRange(equipment);

        var nbtPart = nbt.Count > 0 ? " {" + string.Join(",", nbt) + "}" : "";
        return $"/summon {id} {options.Target}{nbtPart}";
    }

    /// <summary>实体基础属性：Attributes / attributes，1.21.2+ 去掉 generic. 前缀。</summary>
    private static string BuildEntityAttributes(CommandVersion version, List<AttributeEntry> attributes)
    {
        var entries = attributes.Select(entry => version.UsesComponents()
            ? $"{{id:\"{AttributeNbtName(version, entry.Id)}\",base:{entry.Amount:0.##}}}"
            : $"{{Name:\"{AttributeNbtName(version, entry.Id)}\",Base:{entry.Amount:0.##}}}");
        var key = version.UsesComponents() ? "attributes" : "Attributes";
        return key + ":[" + string.Join(",", entries) + "]";
    }

    /// <summary>物品属性修饰：AttributeModifiers（旧 NBT）或 attribute_modifiers（数据组件）。</summary>
    private static string BuildItemAttributes(CommandVersion version, List<AttributeEntry> attributes)
    {
        if (attributes.Count == 0) return "";

        if (version.UsesComponents())
        {
            var entries = attributes.Select(entry =>
                $"{{type:\"{AttributeNbtName(version, entry.Id)}\",amount:{entry.Amount:0.##}," +
                "operation:\"add_value\",slot:\"mainhand\"}");
            return "attribute_modifiers=[" + string.Join(",", entries) + "]";
        }

        var nbtEntries = attributes.Select((entry, index) =>
        {
            var name = AttributeNbtName(version, entry.Id);
            return version == CommandVersion.V1_12
                ? $"{{AttributeName:\"{name}\",Name:\"{name}\",Amount:{entry.Amount:0.##},Operation:0," +
                  $"UUIDLeast:{index + 1},UUIDMost:{index + 2},Slot:\"mainhand\"}}"
                : $"{{AttributeName:\"{name}\",Name:\"{name}\",Amount:{entry.Amount:0.##},Operation:0," +
                  $"UUID:[I;{index + 1},{index + 2},0,0],Slot:\"mainhand\"}}";
        });
        return "AttributeModifiers:[" + string.Join(",", nbtEntries) + "]";
    }

    private static readonly Dictionary<string, string> LegacyAttributeNames = new()
    {
        ["generic.max_health"] = "generic.maxHealth",
        ["generic.armor"] = "generic.armor",
        ["generic.armor_toughness"] = "generic.armorToughness",
        ["generic.attack_damage"] = "generic.attackDamage",
        ["generic.attack_speed"] = "generic.attackSpeed",
        ["generic.movement_speed"] = "generic.movementSpeed",
        ["generic.knockback_resistance"] = "generic.knockbackResistance",
        ["generic.follow_range"] = "generic.followRange",
        ["generic.luck"] = "generic.luck"
    };

    /// <summary>把属性 ID 规范为 generic.xxx，并按版本给出指令里可用的写法。</summary>
    public static string NormalizeAttribute(string id)
    {
        id = (id ?? "").Trim().ToLowerInvariant().Replace("minecraft:", "");
        if (id.Length == 0) return "generic.max_health";
        if (!id.Contains('.')) id = "generic." + id;
        return id;
    }

    private static string AttributeNbtName(CommandVersion version, string id)
    {
        var key = NormalizeAttribute(id);
        if (version == CommandVersion.V1_12)
            return LegacyAttributeNames.TryGetValue(key, out var legacy) ? legacy : key;

        // 1.21.2 起属性 ID 去掉了 generic. 前缀
        return version == CommandVersion.V1_21_5Plus
            ? "minecraft:" + key.Replace("generic.", "")
            : "minecraft:" + key;
    }

    private static List<string> BuildEquipment(CommandVersion version, SummonOptions options)
    {
        var items = new (string Slot, string Id)[]
        {
            ("mainhand", options.MainHand), ("offhand", options.OffHand),
            ("head", options.Head), ("chest", options.Chest),
            ("legs", options.Legs), ("feet", options.Feet)
        };
        var filled = items.Where(i => !string.IsNullOrWhiteSpace(i.Id)).ToList();
        if (filled.Count == 0) return new List<string>();

        if (version.UsesEquipmentComponent())
        {
            var entries = filled.Select(i =>
                $"{i.Slot}:{{id:\"{NormalizeItem(i.Id, version)}\",count:1}}");
            return new List<string> { "equipment:{" + string.Join(",", entries) + "}" };
        }

        // 1.20.5 起物品栈 count 变为小写
        var countKey = version.UsesComponents() ? "count" : "Count";
        var countSuffix = version.UsesComponents() ? "" : "b";

        string Stack(string id) => string.IsNullOrWhiteSpace(id)
            ? "{}"
            : $"{{id:\"{NormalizeItem(id, version)}\",{countKey}:1{countSuffix}}}";

        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.MainHand) || !string.IsNullOrWhiteSpace(options.OffHand))
            result.Add("HandItems:[" + Stack(options.MainHand) + "," + Stack(options.OffHand) + "]");

        var armor = new List<string>
        {
            Stack(options.Feet), Stack(options.Legs), Stack(options.Chest), Stack(options.Head)
        };
        if (armor.Any(a => a != "{}"))
            result.Add("ArmorItems:[" + string.Join(",", armor) + "]");

        return result;
    }


    /// <summary>数据组件参数中的文本（1.21.5+ 可直接写 SNBT 字符串）。</summary>
    public static string TextArgument(CommandVersion version, string text) =>
        TextComponentArgument(version, text);

    private static string TextComponentArgument(CommandVersion version, string text) =>
        version.UsesSnbtText()
            ? "'" + EscapeSnbt(text) + "'"
            : "'{\"text\":\"" + EscapeJson(text) + "\"}'";

    /// <summary>NBT 中的文本组件（实体 CustomName 等）。</summary>
    private static string TextComponentNbt(CommandVersion version, string text) =>
        version.UsesSnbtText()
            ? "'" + EscapeSnbt(text) + "'"
            : JsonNbtText(version, text);

    /// <summary>旧版 NBT 中的 JSON 文本：1.12 需要双引号包裹并转义，1.13+ 可用单引号。</summary>
    private static string JsonNbtText(CommandVersion version, string text) => version == CommandVersion.V1_12
        ? "\"{\\\"text\\\":\\\"" + EscapeJson(text) + "\\\"}\""
        : "'{\"text\":\"" + EscapeJson(text) + "\"}'";

    private static string EscapeJson(string text) =>
        (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeSnbt(string text) =>
        (text ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string NormalizeItem(string id, CommandVersion version)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return "minecraft:stone";
        if (!id.Contains(':')) id = "minecraft:" + id;
        return id.ToLowerInvariant();
    }

    public static string NormalizeEntity(string id, CommandVersion version)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return "minecraft:zombie";
        if (!id.Contains(':')) id = "minecraft:" + id;
        return id.ToLowerInvariant();
    }

    public static string NormalizeEnchant(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return "minecraft:sharpness";
        if (!id.Contains(':')) id = "minecraft:" + id;
        return id.ToLowerInvariant();
    }
}
