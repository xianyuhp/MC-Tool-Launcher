namespace MCTL.Core.Commands;

public record CommandField(string Key, string Label, string Default, string Hint = "");

public record CommandSpec(string Name, string Summary, CommandField[] Fields,
    Func<CommandVersion, Dictionary<string, string>, string> Build);

/// <summary>原版指令目录：覆盖全部指令，give / summon 有独立可视化面板。</summary>
public static class CommandCatalog
{
    private static string V(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";

    private static CommandField F(string key, string label, string @default = "", string hint = "") =>
        new(key, label, @default, hint);

    private static CommandSpec S(string name, string summary, CommandField[] fields,
        Func<CommandVersion, Dictionary<string, string>, string> build) =>
        new(name, summary, fields, build);

    private static CommandSpec Simple(string name, string summary, string template, params CommandField[] fields) =>
        new(name, summary, fields, (_, values) =>
        {
            var text = template;
            foreach (var field in fields)
                text = text.Replace("{" + field.Key + "}", V(values, field.Key));
            return "/" + text;
        });

    public static readonly CommandSpec[] All =
    {
        Simple("gamemode", "切换游戏模式", "gamemode {mode} {target}",
            F("mode", "游戏模式", "survival", "survival / creative / adventure / spectator"),
            F("target", "目标", "@p")),
        Simple("defaultgamemode", "设置默认游戏模式", "defaultgamemode {mode}",
            F("mode", "游戏模式", "survival")),
        Simple("tp", "传送实体", "tp {target} {x} {y} {z}",
            F("target", "目标", "@p"), F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0")),
        Simple("teleport", "传送实体（别名）", "teleport {target} {x} {y} {z}",
            F("target", "目标", "@p"), F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0")),
        Simple("kill", "清除实体", "kill {target}", F("target", "目标", "@e[type=zombie,limit=1]")),
        Simple("clear", "清空物品", "clear {target} {item} {max}",
            F("target", "目标", "@p"), F("item", "物品（可空）", ""), F("max", "最大数量（可空）", "")),
        Simple("give", "给予物品（请用上方 give 面板）", "give {target} {item} {count}",
            F("target", "目标", "@p"), F("item", "物品", "minecraft:diamond"), F("count", "数量", "1")),
        Simple("summon", "生成实体（请用上方 summon 面板）", "summon {entity} {pos}",
            F("entity", "实体", "minecraft:zombie"), F("pos", "坐标", "~ ~ ~")),
        Simple("setworldspawn", "设置世界出生点", "setworldspawn {x} {y} {z}",
            F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0")),
        Simple("spawnpoint", "设置玩家出生点", "spawnpoint {target} {x} {y} {z}",
            F("target", "目标", "@p"), F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0")),
        Simple("seed", "查看世界种子", "seed"),
        Simple("list", "列出在线玩家", "list"),
        Simple("difficulty", "设置难度", "difficulty {level}",
            F("level", "难度", "normal", "peaceful / easy / normal / hard")),
        Simple("time", "设置时间", "time set {value}",
            F("value", "时间", "day", "day / noon / night / midnight / 数字")),
        Simple("weather", "设置天气", "weather {type} {duration}",
            F("type", "天气", "clear", "clear / rain / thunder"), F("duration", "持续时间（秒）", "600")),
        Simple("gamerule", "修改游戏规则", "gamerule {rule} {value}",
            F("rule", "规则名", "keepInventory"), F("value", "值", "true")),

        Simple("setblock", "放置方块", "setblock {x} {y} {z} {block}",
            F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0"), F("block", "方块", "minecraft:stone")),
        Simple("fill", "填充方块", "fill {x1} {y1} {z1} {x2} {y2} {z2} {block}",
            F("x1", "起点 X", "0"), F("y1", "起点 Y", "64"), F("z1", "起点 Z", "0"),
            F("x2", "终点 X", "10"), F("y2", "终点 Y", "64"), F("z2", "终点 Z", "10"),
            F("block", "方块", "minecraft:stone")),
        Simple("clone", "复制区域方块", "clone {x1} {y1} {z1} {x2} {y2} {z2} {dx} {dy} {dz}",
            F("x1", "起点 X", "0"), F("y1", "起点 Y", "64"), F("z1", "起点 Z", "0"),
            F("x2", "终点 X", "5"), F("y2", "终点 Y", "64"), F("z2", "终点 Z", "5"),
            F("dx", "目标 X", "10"), F("dy", "目标 Y", "64"), F("dz", "目标 Z", "0")),
        Simple("forceload", "强制加载区块", "forceload add {x} {z}",
            F("x", "区块 X", "0"), F("z", "区块 Z", "0")),
        Simple("locate", "定位结构 / 生物群系", "locate structure {structure}",
            F("structure", "结构 ID", "minecraft:village_plains")),
        Simple("worldborder", "世界边界", "worldborder {args}",
            F("args", "参数", "set 1000", "center / set / add / damage / get ...")),

        new("effect", "添加 / 清除状态效果", new[]
            {
                F("action", "操作", "give", "give / clear"),
                F("target", "目标", "@p"),
                F("effect", "效果", "minecraft:speed"),
                F("seconds", "持续秒数", "30"),
                F("amplifier", "等级（0 起）", "0")
            },
            (version, v) => version == CommandVersion.V1_12
                ? $"/effect {V(v, "target")} {V(v, "effect")} {V(v, "seconds")} {V(v, "amplifier")}"
                : $"/effect {V(v, "action")} {V(v, "target")} {V(v, "effect")} {V(v, "seconds")} {V(v, "amplifier")}"),
        Simple("enchant", "给手持物品附魔", "enchant {target} {enchantment} {level}",
            F("target", "目标", "@p"), F("enchantment", "附魔", "minecraft:sharpness"), F("level", "等级", "5")),
        new("xp", "经验值", new[] { F("amount", "数量", "100"), F("target", "目标", "@p") },
            (version, v) => version == CommandVersion.V1_12
                ? $"/xp {V(v, "amount")} {V(v, "target")}"
                : $"/xp add {V(v, "amount")} points {V(v, "target")}"),
        new("experience", "经验值（别名）", new[] { F("amount", "数量", "100"), F("target", "目标", "@p") },
            (version, v) => version == CommandVersion.V1_12
                ? $"/xp {V(v, "amount")} {V(v, "target")}"
                : $"/xp add {V(v, "amount")} points {V(v, "target")}"),
        Simple("attribute", "修改属性", "attribute {target} {attribute} base set {value}",
            F("target", "目标", "@p"), F("attribute", "属性", "minecraft:generic.max_health"),
            F("value", "值", "40")),
        Simple("damage", "造成伤害", "damage {target} {amount} {type}",
            F("target", "目标", "@p"), F("amount", "伤害", "5"),
            F("type", "伤害类型", "minecraft:generic")),
        Simple("ride", "骑乘实体", "ride {target} mount {vehicle}",
            F("target", "乘客", "@p"), F("vehicle", "载具", "@e[type=minecraft:horse,limit=1]")),

        new("title", "标题 / 副标题 / 动作栏", new[]
            {
                F("target", "目标", "@a"),
                F("action", "位置", "title", "title / subtitle / actionbar"),
                F("text", "文本", "欢迎来到服务器")
            },
            (version, v) =>
                $"/title {V(v, "target")} {V(v, "action")} {CommandSyntax.TextArgument(version, V(v, "text"))}"),
        Simple("tellraw", "发送原始 JSON 文本", "tellraw {target} {json}",
            F("target", "目标", "@a"), F("json", "JSON 文本", "{\"text\":\"Hello\",\"color\":\"gold\"}")),
        Simple("say", "发送聊天消息", "say {message}", F("message", "消息", "Hello")),
        Simple("me", "发送动作消息", "me {action}", F("action", "动作", "打了个招呼")),
        Simple("msg", "私聊消息", "msg {target} {message}",
            F("target", "目标", "Steve"), F("message", "消息", "你好")),
        Simple("tell", "私聊消息（别名）", "tell {target} {message}",
            F("target", "目标", "Steve"), F("message", "消息", "你好")),
        Simple("teammsg", "发送队伍消息", "teammsg {message}", F("message", "消息", "集合！")),
        Simple("playsound", "播放音效", "playsound {sound} {source} {target} {x} {y} {z} {volume} {pitch}",
            F("sound", "音效", "minecraft:entity.player.levelup"), F("source", "声道", "master"),
            F("target", "目标", "@a"), F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0"),
            F("volume", "音量", "1"), F("pitch", "音调", "1")),
        Simple("stopsound", "停止音效", "stopsound {target} {source} {sound}",
            F("target", "目标", "@a"), F("source", "声道", "master"), F("sound", "音效", "*")),
        Simple("particle", "生成粒子", "particle {particle} {x} {y} {z} {dx} {dy} {dz} {speed} {count}",
            F("particle", "粒子", "minecraft:flame"), F("x", "X", "0"), F("y", "Y", "64"), F("z", "Z", "0"),
            F("dx", "扩散 X", "0.2"), F("dy", "扩散 Y", "0.2"), F("dz", "扩散 Z", "0.2"),
            F("speed", "速度", "0"), F("count", "数量", "20")),
        Simple("bossbar", "Boss 血条", "bossbar {args}", F("args", "参数", "add minecraft:custom my_boss {\"text\":\"Boss\"}")),
        Simple("spreadplayers", "随机分散玩家", "spreadplayers {x} {z} {spreadDistance} {maxRange} {target}",
            F("x", "中心 X", "0"), F("z", "中心 Z", "0"), F("spreadDistance", "最小间距", "10"),
            F("maxRange", "最大范围", "50"), F("target", "目标", "@a")),
        Simple("spectate", "旁观实体", "spectate {target}",
            F("target", "目标", "@e[type=minecraft:zombie,limit=1]")),

        Simple("data", "读写方块 / 实体 / 存储 NBT", "data {args}",
            F("args", "参数", "get entity @s Pos", "get / merge / modify ...")),
        Simple("execute", "执行子命令", "execute {args}",
            F("args", "参数", "as @a at @s run say hi")),
        Simple("scoreboard", "计分板", "scoreboard {args}",
            F("args", "参数", "objectives add kills dummy 击杀数")),
        Simple("tag", "实体标签", "tag {target} add {tag}",
            F("target", "目标", "@p"), F("tag", "标签", "vip")),
        Simple("team", "队伍", "team {args}",
            F("args", "参数", "add red")),
        Simple("advancement", "进度", "advancement {args}",
            F("args", "参数", "grant @p everything")),
        Simple("recipe", "解锁 / 锁定配方", "recipe {args}",
            F("args", "参数", "give @p minecraft:diamond_sword")),
        Simple("loot", "掉落 / 给予战利品表", "loot give @p loot {lootTable}",
            F("lootTable", "战利品表", "minecraft:chests/simple_dungeon")),
        Simple("item", "修改容器物品", "item {args}",
            F("args", "参数", "replace entity @s armor.head with minecraft:diamond_helmet")),
        Simple("place", "放置结构 / 地物", "place {args}",
            F("args", "参数", "feature minecraft:village_plains ~ ~ ~")),
        Simple("random", "随机数", "random {args}",
            F("args", "参数", "value 1..6")),
        Simple("function", "运行函数", "function {name}", F("name", "函数", "namespace:function")),
        Simple("schedule", "计划执行函数", "schedule function {name} {time}",
            F("name", "函数", "namespace:function"), F("time", "延迟", "10s")),
        Simple("datapack", "数据包管理", "datapack {args}", F("args", "参数", "list")),
        Simple("reload", "重载数据包", "reload"),
        Simple("tick", "控制游戏刻", "tick {args}", F("args", "参数", "freeze")),
        Simple("jfr", "JFR 性能分析", "jfr {args}", F("args", "参数", "start")),
        Simple("debug", "调试", "debug {args}", F("args", "参数", "start")),

        Simple("op", "授予管理员", "op {target}", F("target", "玩家", "Steve")),
        Simple("deop", "取消管理员", "deop {target}", F("target", "玩家", "Steve")),
        Simple("kick", "踢出玩家", "kick {target} {reason}",
            F("target", "玩家", "Steve"), F("reason", "原因", "行为不当")),
        Simple("ban", "封禁玩家", "ban {target} {reason}",
            F("target", "玩家", "Steve"), F("reason", "原因", "违反规则")),
        Simple("pardon", "解除封禁", "pardon {target}", F("target", "玩家", "Steve")),
        Simple("banlist", "查看封禁列表", "banlist {type}", F("type", "类型", "players")),
        Simple("whitelist", "白名单", "whitelist {action} {target}",
            F("action", "操作", "add"), F("target", "玩家（可空）", "Steve")),
        Simple("save-all", "保存世界", "save-all"),
        Simple("save-on", "开启自动保存", "save-on"),
        Simple("save-off", "关闭自动保存", "save-off"),
        Simple("stop", "关闭服务器", "stop"),
        Simple("publish", "局域网开放", "publish"),
        Simple("transfer", "转移玩家到其它服务器", "transfer {host} {port} {target}",
            F("host", "主机", "example.com"), F("port", "端口", "25565"), F("target", "玩家", "@a")),
        Simple("setidletimeout", "挂机踢出时间", "setidletimeout {minutes}", F("minutes", "分钟", "10")),
        Simple("help", "帮助", "help {command}", F("command", "指令（可空）", ""))
    };

    public static readonly string[] Categories =
    {
        "基础", "世界与方块", "实体与效果", "文本与界面", "数据与进度", "管理"
    };

    public static CommandSpec Find(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
