using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>
/// Конфиг sing-box (ветка 1.11) для режима «Весь компьютер».
/// Весь трафик → виртуальный адаптер → локальный SOCKS Xray (там решается: через VPN или напрямую для российских сайтов).
/// Мимо адаптера: сам Xray и это приложение (иначе петля), локальная сеть и мультикаст (принтеры, роутер, ТВ).
/// </summary>
internal static class TunConfigBuilder
{
    public const string InterfaceName = "Siberia Connect";

    public static string Build(int socksPort)
    {
        var cfg = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn", ["timestamp"] = false },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray(
                    new JsonObject { ["tag"] = "remote", ["address"] = "tcp://1.1.1.1", ["detour"] = "proxy" },
                    new JsonObject { ["tag"] = "local", ["address"] = "local" }),
                ["rules"] = new JsonArray(new JsonObject { ["outbound"] = "any", ["server"] = "local" }),
                ["final"] = "remote",
                ["strategy"] = "ipv4_only"
            },
            ["inbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["interface_name"] = InterfaceName,
                ["address"] = new JsonArray("172.19.0.1/30"),
                ["mtu"] = 9000,
                ["auto_route"] = true,
                ["strict_route"] = false,      // не ломаем обнаружение принтеров и устройств в локальной сети
                ["stack"] = "mixed",
                ["sniff"] = true,
                ["sniff_override_destination"] = true   // Xray видит домены — российские сайты идут напрямую
            }),
            ["outbounds"] = new JsonArray(
                new JsonObject { ["type"] = "socks", ["tag"] = "proxy", ["server"] = "127.0.0.1", ["server_port"] = socksPort, ["version"] = "5" },
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                new JsonObject { ["type"] = "dns", ["tag"] = "dns-out" }),
            ["route"] = new JsonObject
            {
                ["auto_detect_interface"] = true,
                ["rules"] = new JsonArray(
                    new JsonObject { ["protocol"] = "dns", ["outbound"] = "dns-out" },
                    new JsonObject { ["process_name"] = new JsonArray("xray.exe", "SiberiaConnect.exe", "sing-box.exe"), ["outbound"] = "direct" },
                    new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" },
                    new JsonObject { ["ip_cidr"] = new JsonArray("224.0.0.0/4", "255.255.255.255/32"), ["outbound"] = "direct" }),
                ["final"] = "proxy"
            }
        };
        return cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
