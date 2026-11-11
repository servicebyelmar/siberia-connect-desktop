using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>
/// Конфиг sing-box (ветка 1.11) для режима «Весь компьютер».
/// Весь трафик → виртуальный адаптер → локальный SOCKS Xray (там решается: через VPN или напрямую для российских сайтов).
/// Мимо адаптера: адрес VPN-сервера (на уровне маршрутов Windows), сам Xray и это приложение,
/// локальная сеть и мультикаст (принтеры, роутер, ТВ).
/// </summary>
internal static class TunConfigBuilder
{
    public const string InterfaceName = "Siberia Connect";
    public const string TunIp4 = "172.30.233.1", TunIp6 = "fdfe:dcba:7233::1";

    /// <summary>IPv4-адреса сервера из ссылки vless://…@host:port — их трафик идёт мимо адаптера.</summary>
    public static string[] ServerIps(string link)
    {
        try
        {
            var host = new Uri(link).Host.Trim('[', ']');
            if (IPAddress.TryParse(host, out var ip))
                return ip.AddressFamily == AddressFamily.InterNetwork ? new[] { ip + "/32" } : Array.Empty<string>();
            var t = Dns.GetHostAddressesAsync(host);
            if (!t.Wait(3000)) return Array.Empty<string>();
            return t.Result.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                           .Select(a => a + "/32").Distinct().ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    public static string Build(int socksPort, string[] excludeIps = null)
    {
        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["interface_name"] = InterfaceName,
            // 1.3.1: свой редкий адрес — 172.19.0.1 по умолчанию занимают Happ и другие клиенты на sing-box («The object already exists»)
            ["address"] = new JsonArray(TunIp4 + "/30", TunIp6 + "/126"),   // IPv6 тоже в туннель — иначе Chrome уходит по IPv6 мимо VPN
            ["mtu"] = 9000,
            ["auto_route"] = true,
            ["strict_route"] = false,      // не ломаем обнаружение принтеров и устройств в локальной сети
            ["stack"] = "mixed",
            ["sniff"] = true,
            ["sniff_override_destination"] = true   // Xray видит домены — российские сайты идут напрямую
        };
        if (excludeIps is { Length: > 0 })
        {
            // туннель до сервера не заходит в адаптер вообще: нет петли и лишнего круга → полная скорость
            var arr = new JsonArray();
            foreach (var ip in excludeIps) arr.Add((JsonNode)ip);   // явное JsonNode: без рефлексии сериализатора
            tun["route_exclude_address"] = arr;
        }

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
            ["inbounds"] = new JsonArray(tun),
            ["outbounds"] = new JsonArray(
                new JsonObject { ["type"] = "socks", ["tag"] = "proxy", ["server"] = "127.0.0.1", ["server_port"] = socksPort, ["version"] = "5" },
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }),
            ["route"] = new JsonObject
            {
                ["auto_detect_interface"] = true,
                ["rules"] = new JsonArray(
                    // первым: Xray и приложение ходят напрямую — в том числе их собственные DNS-запросы (иначе круг через туннель и тормоза)
                    new JsonObject { ["process_name"] = new JsonArray("xray.exe", "SiberiaConnect.exe", "sing-box.exe"), ["outbound"] = "direct" },
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    // QUIC (UDP 443) отклоняем: браузер и YouTube сразу переходят на TCP — через VLESS это быстрее и стабильнее
                    new JsonObject { ["network"] = "udp", ["port"] = 443, ["action"] = "reject", ["no_drop"] = true },
                    new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" },
                    new JsonObject { ["ip_cidr"] = new JsonArray("224.0.0.0/4", "255.255.255.255/32"), ["outbound"] = "direct" }),
                ["final"] = "proxy"
            }
        };
        return cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
