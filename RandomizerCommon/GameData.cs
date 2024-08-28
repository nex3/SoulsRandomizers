using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SoulsFormats;
using SoulsIds;
using YamlDotNet.Serialization;
using static RandomizerCommon.LocationData;
using static RandomizerCommon.Util;
using static SoulsIds.GameSpec;

namespace RandomizerCommon
{
    public class GameData
    {
        private static readonly List<string> itemParams = new List<string>()
        {
            "EquipParamWeapon", "EquipParamProtector", "EquipParamAccessory", "EquipParamGoods", "EquipParamGem",
        };

        public static readonly ISerializer Serializer = new SerializerBuilder()
            .DisableAliases()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();

        public readonly GameEditor Editor;
        public FromGame Type => Editor.Spec.Game;
        public bool Sekiro => Type == FromGame.SDT;
        public bool DS3 => Type == FromGame.DS3;
        public bool EldenRing => Type == FromGame.ER;

        /// <summary>Returns the path to the installation directory for the current game.</summary>
        public string InstallPath
        {
            get
            {
                var parent = SteamPath.SteamPath.Find(Type switch
                {
                    FromGame.DS3 => "374320",
                    FromGame.SDT => "814380",
                    FromGame.ER => "1245620",
                    _ => throw new NotImplementedException(),
                }) ?? throw new Exception("Can't find game executable, is it installed?");
                return $@"{parent}\Game";
            }
        }

        public Dictionary<string, string> BhdKeys
        {
            get => Type switch
            {
                FromGame.DS3 => new()
                {
                    ["Data1"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEA05hqyboW/qZaJ3GBIABFVt1X1aa0/sKINklvpkTRC+5Ytbxvp18L
M1gN6gjTgSJiPUgdlaMbptVa66MzvilEk60aHyVVEhtFWy+HzUZ3xRQm6r/2qsK3
8wXndgEU5JIT2jrBXZcZfYDCkUkjsGVkYqjBNKfp+c5jlnNwbieUihWTSEO+DA8n
aaCCzZD3e7rKhDQyLCkpdsGmuqBvl02Ou7QeehbPPno78mOYs2XkP6NGqbFFGQwa
swyyyXlQ23N15ZaFGRRR0xYjrX4LSe6OJ8Mx/Zkec0o7L28CgwCTmcD2wO8TEATE
AUbbV+1Su9uq2+wQxgnsAp+xzhn9og9hmwIEC35bSQ==
-----END RSA PUBLIC KEY-----",

                    ["Data2"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAvCZAK9UfPdk5JaTlG7n1r0LSVzIan3h0BSLaMXQHOwO7tTGpvtdX
m2ZLY9y8SVmOxWTQqRq14aVGLTKDyH87hPuKd47Y0E5K5erTqBbXW6AD4El1eir2
VJz/pwHt73FVziOlAnao1A5MsAylZ9B5QJyzHJQG+LxzMzmWScyeXlQLOKudfiIG
0qFw/xhRMLNAI+iypkzO5NKblYIySUV5Dx7649XdsZ5UIwJUhxONsKuGS+MbeTFB
mTMehtNj5EwPxGdT4CBPAWdeyPhpoHJHCbgrtnN9akwQmpwdBBxT/sTD16Adn9B+
TxuGDQQALed4S4KvM+fadx27pQz8pP9VLwIEL67iCQ==
-----END RSA PUBLIC KEY-----",

                    ["Data3"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAqLytWD20TSXPeAA1RGDwPW18nJwe2rBX+0HPtdzFmQc/KmQlWrP+
94k6KClK5f7m0xUHwT8+yFGLxPdRvUPyOhBEnRA6tkObVDSxij5y0Jh4h4ilAO73
I8VMcmscS71UKkck4444+eR4vVd+SPlzIu8VgqLefvEn/sX/pAevDp7w+gD0NgvO
e9U6iWEXKwTOPB97X+Y2uB03gSSognmV8h2dtUFJ4Ryn5jrpWmsuUbdvGp0CWBKH
CFruNXnfsG0hlf9LqbVmEzbFl/MhjBmbVjjtelorZsoLPK+OiPTHW5EcwwnPh1vH
FFGM7qRMc0yvHqJnniEWDsSz8Bvg+GxpgQIEC8XNVw==
-----END RSA PUBLIC KEY-----",

                    ["Data4"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEArfUaZWjYAUaZ0q+5znpX55GeyepawCZ5NnsMjIW9CA3vrOgUGRkh
6aAU9frlafQ81LQMRgAznOnQGE7K3ChfySDpq6b47SKm4bWPqd7Ulh2DTxIgi6QP
qm4UUJL2dkLaCnuoya/pGMOOvhT1LD/0CKo/iKwfBcYf/OAnwSnxMRC3SNRugyvF
ylCet9DEdL5L8uBEa4sV4U288ZxZSZLg2tB10xy5SHAsm1VNP4Eqw5iJbqHEDKZW
n2LJP5t5wpEJvV2ACiA4U5fyjQLDzRwtCKzeK7yFkKiZI95JJhU/3DnVvssjIxku
gYZkS9D3k9m+tkNe0VVrd4mBEmqVxg+V9wIEL6Y6tw==
-----END RSA PUBLIC KEY-----",

                    ["Data5"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAvKTlU3nka4nQesRnYg1NWovCCTLhEBAnjmXwI69lFYfc4lvZsTrQ
E0Y25PtoP0ZddA3nzflJNz1rBwAkqfBRGTeeTCAyoNp/iel3EAkid/pKOt3JEkHx
rojRuWYSQ0EQawcBbzCfdLEjizmREepRKHIUSDWgu0HTmwSFHHeCFbpBA1h99L2X
izH5XFTOu0UIcUmBLsK6DYsIj5QGrWaxwwXcTJN/X+/syJ/TbQK9W/TCGaGiirGM
1u2wvZXSZ7uVM3CHwgNhAMiqLvqORygcDeNqxgq+dXDTxka43j7iPJWdHs8b25fy
aH3kbUxKlDGaEENNNyZQcQrgz8Q76jIE0QIEFUsz9w==
-----END RSA PUBLIC KEY-----",

                    ["DLC1"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAsCGM9dFwzaIOUIin3DXy7xrmI2otKGLZJQyKi5X3znKhSTywpcFc
KoW6hgjeh4fJW24jhzwBosG6eAzDINm+K02pHCG8qZ/D/hIbu+ui0ENDKqrVyFhn
QtX5/QJkVQtj8M4a0FIfdtE3wkxaKtP6IXWIy4DesSdGWONVWLfi2eq62A5ts5MF
qMoSV3XjTYuCgXqZQ6eOE+NIBQRqpZxLNFSzbJwWXpAg2kBMkpy5+ywOByjmWzUw
jnIFl1T17R8DpTU/93ojx+/q1p+b1o5is5KcoP7QwjOqzjHJH8bTytzRbgmRcDMW
3ahxgI070d45TMXK2YwRzI6/JbM1P29anQIEFezyYw==
-----END RSA PUBLIC KEY-----",

                    ["DLC2"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAtCXU9a/GBMVoqtpQox9p0/5sWPaIvDp8avLFnIBhN7vkgTwulZHi
u64vZAiUAdVeFX4F+Qtk+5ivK488Mu2CzAMJcz5RvyMQJtOQXuDDqzIv21Tr5zuu
sswoErHxxP8TZNxkHm7Ram7Oqtn7LQnMTYxsBgZZ34yJkRtAmZnGoCu5YaUR5euk
8lF75idi97ssczUNV212tLzIMa1YOV7sxOb7+gc0VTIqs3pa+OXLPI/bMfwUc/KN
jur5aLDDntQHGx5zuNtc78gMGwlmPqDhgTusKPO4VyKvoL0kITYvukoXJATaa1HI
WVUjhLm+/uj8r8PNgolerDeS+8FM5Bpe9QIEHwCZLw==
-----END RSA PUBLIC KEY-----",
                },
                FromGame.SDT => new()
                {
                    ["Data1"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEA92l+AWx1aV7mzt+6r00bm/qnc4b6NH3VVr/v4UxMcfzushL8jsn9
ZSP1ss95ot/quk8dOJsp0+/bvxH+C9DEezzNLSqqAGd2jq2PYosj/6FhYAKjjMlK
jNxcVPsKQug0Zby+KYsENirmEXcmA1fzltrISf6d6LKB1UFHHN9NRkLCm3idE4Pu
9852kPHbiL14EqfDCDgwm7kLeQdt3kUbcmdhu/6dvP42HGxBmAYLNFD3iAe7qLML
MFzmKKHQD2fRQK/431Z3xPK6Jp245AdR0AwUYVvnXq+/97wMX0C6UKvAZ+b/1ytD
Nu8vZt++lhJ01SjTc2A4hVPz7g1EEO5/TQIEKkj5Jw==
-----END RSA PUBLIC KEY-----",

                    ["Data2"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBDAKCAQEAqhjoThWX8VwsTKTI1kjp0JBloCXhV8i99P1KPTCTDBnmhVQPdu+7
UQ5g4//eh0oqKaOUjet+0SP94QscjIIrhV91OzfIouIWgJJK/ROOP/A3sb5AlzPa
6YPcN8ODxR+esyrWhc6rHCt4qGvXVXrgh6zpZM5h5VCTSaup4qqIWm44EF3+FeYS
7faFg14rH0QEosieIIZFZmpI6SCJanlrVd+Zh13s4XcZfk0JdC2AEjxCQ2lKi3Un
WAMOcJc+8uHoMuNNo1PMpYQ6Z8Nzg5Cii7EnwbCDmuJw58tFBmbOVHZpkY93VIeF
maJXSE7ztTp0qTa05YZUsiU3g9HplkeTUwIFAP/xKZE=
-----END RSA PUBLIC KEY-----",

                    ["Data3"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBDAKCAQEAx5jlgIvoHQLwSFsAwKFZbNo3fgZ89C7tj4hwiZsQVg8QnNZohXl5
S5Ep9pS2biOFsSkuZMXKmfYErh2CsdFbr7QR7kvPPianXNrkCI4xlfQwJvMmkLm9
6/JmRIUzTWp0kKJUJZJH/UIrXNn7fmk8Vmx1bQIi8bumGSl3gxeMhutv/lC9khsY
Tn0ABTJAbIbwNZ5GPXxzQZuQPXXDY52Gm+Fx7Yy1LiK/B6isIDJUN0xdgxdaXxGN
f5pPocMJjng0Ob3cjhGvdkysll/jYFnRx0La3CGmtLcXMtHheEQxzGueGDa/lkkl
AvvEXtcpKfyFQWcUheQZ8LngAh/UTJHtQwIFAOpVoU8=
-----END RSA PUBLIC KEY-----",

                    ["Data4"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAq8RyArk+eqMAcxLAHUDRYV7yScNKZpKSxGmgJZQ7y6Y8f5wdrNCt
byXfmsdQECStIGlkwWjtfm8t/bRZuxxPciAYaFsWo0Ze2BB6uY6ZteNpLJn82qbL
TXATf+af3kSrvICfvJwRzbfA/PRJRkHj2gJ6Tc7g6HK7S/4TiCZirq+c/zLY3gb8
A8uIFNI4j0qxTzfoAlS7K6spZjfnhZ6l7pYFh+glz15wAbppC9Oy/u5vUacozf4v
nacbUHD47ds9EZPZDHk3LfJbioHwtUzJfyBqZmIpI33yiwImPpb96zwvQU86TaXK
sJrTmSs/48BeDsQwXuaqOg+6noETBx3pgQIEGM2Ohw==
-----END RSA PUBLIC KEY-----",

                    ["Data5"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBDAKCAQEAu75/UbXwHdvu/p49TwnY7Ou6DAuZYFAtLUkw/R4nvm0HWVlRsZiB
LG3MOG6sPmK2Zc3JLBU2QK4uKazZ9VrmotM4OpYr03q2tiFnv3NfCvB1UeIJIKe3
kVhHNZIbvrwEP9a5UCnrSHD+u+Fj5MQBr4yrEitwrNVvIC4J0Ez1Ppn3+D8ff8Xg
QRP9qCVLI3X/wdQDea+B5o8PWaYEL9MKnnL1Tq4h+4PRYHcQR8/GXBTrc3x9q3cP
QRDWHbRYhIfWSP9urtagjcsmcuG+p34fp+KyWOwkil3FJqwH1KgSTbk9Tb0oBPzq
TCJKeE/wgu6hY++lBi5T3ArHZZcsbXzV6wIFAPlRTMc=
-----END RSA PUBLIC KEY-----",
                },
                FromGame.ER => new()
                {
                    ["Data0"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEA9Rju2whruXDVQZpfylVEPeNxm7XgMHcDyaaRUIpXQE0qEo+6Y36L
P0xpFvL0H0kKxHwpuISsdgrnMHJ/yj4S61MWzhO8y4BQbw/zJehhDSRCecFJmFBz
3I2JC5FCjoK+82xd9xM5XXdfsdBzRiSghuIHL4qk2WZ/0f/nK5VygeWXn/oLeYBL
jX1S8wSSASza64JXjt0bP/i6mpV2SLZqKRxo7x2bIQrR1yHNekSF2jBhZIgcbtMB
xjCywn+7p954wjcfjxB5VWaZ4hGbKhi1bhYPccht4XnGhcUTWO3NmJWslwccjQ4k
sutLq3uRjLMM0IeTkQO6Pv8/R7UNFtdCWwIERzH8IQ==
-----END RSA PUBLIC KEY-----",

                    ["Data1"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAxaBCHQJrtLJiJNdG9nq3deA9sY4YCZ4dbTOHO+v+YgWRMcE6iK6o
ZIJq+nBMUNBbGPmbRrEjkkH9M7LAypAFOPKC6wMHzqIMBsUMuYffulBuOqtEBD11
CAwfx37rjwJ+/1tnEqtJjYkrK9yyrIN6Y+jy4ftymQtjk83+L89pvMMmkNeZaPON
4O9q5M9PnFoKvK8eY45ZV/Jyk+Pe+xc6+e4h4cx8ML5U2kMM3VDAJush4z/05hS3
/bC4B6K9+7dPwgqZgKx1J7DBtLdHSAgwRPpijPeOjKcAa2BDaNp9Cfon70oC+ZCB
+HkQ7FjJcF7KaHsH5oHvuI7EZAl2XTsLEQIENa/2JQ==
-----END RSA PUBLIC KEY-----",

                    ["Data2"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBDAKCAQEA0iDVVQ230RgrkIHJNDgxE7I/2AaH6Li1Eu9mtpfrrfhfoK2e7y4O
WU+lj7AGI4GIgkWpPw8JHaV970Cr6+sTG4Tr5eMQPxrCIH7BJAPCloypxcs2BNfT
GXzm6veUfrGzLIDp7wy24lIA8r9ZwUvpKlN28kxBDGeCbGCkYeSVNuF+R9rN4OAM
RYh0r1Q950xc2qSNloNsjpDoSKoYN0T7u5rnMn/4mtclnWPVRWU940zr1rymv4Jc
3umNf6cT1XqrS1gSaK1JWZfsSeD6Dwk3uvquvfY6YlGRygIlVEMAvKrDRMHylsLt
qqhYkZNXMdy0NXopf1rEHKy9poaHEmJldwIFAP////8=
-----END RSA PUBLIC KEY-----",

                    ["Data3"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAvRRNBnVq3WknCNHrJRelcEA2v/OzKlQkxZw1yKll0Y2Kn6G9ts94
SfgZYbdFCnIXy5NEuyHRKrxXz5vurjhrcuoYAI2ZUhXPXZJdgHywac/i3S/IY0V/
eDbqepyJWHpP6I565ySqlol1p/BScVjbEsVyvZGtWIXLPDbx4EYFKA5B52uK6Gdz
4qcyVFtVEhNoMvg+EoWnyLD7EUzuB2Khl46CuNictyWrLlIHgpKJr1QD8a0ld0PD
PHDZn03q6QDvZd23UW2d9J+/HeBt52j08+qoBXPwhndZsmPMWngQDaik6FM7EVRQ
etKPi6h5uprVmMAS5wR/jQIVTMpTj/zJdwIEXszeQw==
-----END RSA PUBLIC KEY-----",

                    ["DLC"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAmYJ/5GJU4boJSvZ81BFOHYTGdBWPHnWYly3yWo01BYjGRnz8NTkz
DHUxsbjIgtG5XqsQfZstZILQ97hgSI5AaAoCGrT8sn0PeXg2i0mKwL21gRjRUdvP
Dp1Y+7hgrGwuTkjycqqsQ/qILm4NvJHvGRd7xLOJ9rs2zwYhceRVrq9XU2AXbdY4
pdCQ3+HuoaFiJ0dW0ly5qdEXjbSv2QEYe36nWCtsd6hEY9LjbBX8D1fK3D2c6C0g
NdHJGH2iEONUN6DMK9t0v2JBnwCOZQ7W+Gt7SpNNrkx8xKEM8gH9na10g9ne11Mi
O1FnLm8i4zOxVdPHQBKICkKcGS1o3C2dfwIEXw/f3w==
-----END RSA PUBLIC KEY-----",

                    ["sd\\sd"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAmYJ/5GJU4boJSvZ81BFOHYTGdBWPHnWYly3yWo01BYjGRnz8NTkz
DHUxsbjIgtG5XqsQfZstZILQ97hgSI5AaAoCGrT8sn0PeXg2i0mKwL21gRjRUdvP
Dp1Y+7hgrGwuTkjycqqsQ/qILm4NvJHvGRd7xLOJ9rs2zwYhceRVrq9XU2AXbdY4
pdCQ3+HuoaFiJ0dW0ly5qdEXjbSv2QEYe36nWCtsd6hEY9LjbBX8D1fK3D2c6C0g
NdHJGH2iEONUN6DMK9t0v2JBnwCOZQ7W+Gt7SpNNrkx8xKEM8gH9na10g9ne11Mi
O1FnLm8i4zOxVdPHQBKICkKcGS1o3C2dfwIEXw/f3w==
-----END RSA PUBLIC KEY-----",

                    ["sd\\sd_dlc02"] =
@"-----BEGIN RSA PUBLIC KEY-----
MIIBCwKCAQEAmYJ/5GJU4boJSvZ81BFOHYTGdBWPHnWYly3yWo01BYjGRnz8NTkz
DHUxsbjIgtG5XqsQfZstZILQ97hgSI5AaAoCGrT8sn0PeXg2i0mKwL21gRjRUdvP
Dp1Y+7hgrGwuTkjycqqsQ/qILm4NvJHvGRd7xLOJ9rs2zwYhceRVrq9XU2AXbdY4
pdCQ3+HuoaFiJ0dW0ly5qdEXjbSv2QEYe36nWCtsd6hEY9LjbBX8D1fK3D2c6C0g
NdHJGH2iEONUN6DMK9t0v2JBnwCOZQ7W+Gt7SpNNrkx8xKEM8gH9na10g9ne11Mi
O1FnLm8i4zOxVdPHQBKICkKcGS1o3C2dfwIEXw/f3w==
-----END RSA PUBLIC KEY-----",
                },
                _ => throw new NotImplementedException()
            };
        }

        public readonly string Dir;
        private string ModDir;

        // Informational data
        // TODO: Perhaps have this data in configs
        private static readonly Dictionary<string, string> DS3LocationNames = new Dictionary<string, string>
        {
            { "m30_00_00_00", "highwall" },
            { "m30_01_00_00", "lothric" },
            { "m34_01_00_00", "archives" },
            { "m31_00_00_00", "settlement" },
            { "m32_00_00_00", "archdragon" },
            { "m33_00_00_00", "farronkeep" },
            { "m35_00_00_00", "cathedral" },
            { "m37_00_00_00", "irithyll" },
            { "m38_00_00_00", "catacombs" },
            { "m39_00_00_00", "dungeon" },
            { "m40_00_00_00", "firelink" },
            { "m41_00_00_00", "kiln" },
            { "m45_00_00_00", "ariandel" },
            { "m50_00_00_00", "dregheap" },
            { "m51_00_00_00", "ringedcity" },
            { "m51_01_00_00", "filianore" },
        };
        private static Dictionary<string, string> DS3MapNames = new Dictionary<string, string>
        {
            { "", "Global" },
            { "highwall", "High Wall" },
            { "lothric", "Lothric Castle" },
            { "archives", "Grand Archives" },
            { "settlement", "Undead Settlement" },
            { "archdragon", "Archdragon Peak" },
            { "farronkeep", "Farron Keep" },
            { "cathedral", "Cathedral" },
            { "irithyll", "Irithyll" },
            { "catacombs", "Catacombs" },
            { "dungeon", "Irithyll Dungeon" },
            { "firelink", "Firelink Shrine" },
            { "kiln", "Kiln" },
            { "ariandel", "Ariandel" },
            { "dregheap", "Dreg Heap" },
            { "ringedcity", "Ringed City" },
            { "filianore", "Filianore's Rest" },
            // Overriden names for more specific display for bosses
            { "cemetery", "Cemetery of Ash" },
            { "lake", "Smouldering Lake" },
            { "anorlondo", "Anor Londo" },
            { "profaned", "Profaned Capital" },
            { "garden", "Consumed King's Garden" },
            { "untended", "Untended Graves" },
        };
        private static readonly Dictionary<string, string> SekiroLocationNames = new Dictionary<string, string>
        {
            { "m10_00_00_00", "hirata" },
            { "m11_00_00_00", "ashinaoutskirts" },
            { "m11_01_00_00", "ashinacastle" },
            { "m11_02_00_00", "ashinareservoir" },
            { "m13_00_00_00", "dungeon" },
            { "m15_00_00_00", "mibuvillage" },
            { "m17_00_00_00", "sunkenvalley" },
            { "m20_00_00_00", "senpou" },
            { "m25_00_00_00", "fountainhead" },
        };
        private static Dictionary<string, string> SekiroMapNames = new Dictionary<string, string>
        {
            { "", "Global" },
            { "hirata", "Hirata Estate" },
            { "ashinaoutskirts", "Ashina Outskirts" },
            { "ashinacastle", "Ashina Castle" },
            { "ashinareservoir", "Ashina Reservoir" },
            { "dungeon", "Abandoned Dungeon" },
            { "mibuvillage", "Ashina Depths" },
            { "sunkenvalley", "Sunken Valley" },
            { "senpou", "Senpou Temple" },
            { "fountainhead", "Fountainhead Palace" },
        };
        private readonly static Dictionary<uint, ItemType> MaskLotItemTypes = new Dictionary<uint, ItemType>
        {
            [0x00000000] = ItemType.WEAPON,
            [0x10000000] = ItemType.ARMOR,
            [0x20000000] = ItemType.RING,
            [0x40000000] = ItemType.GOOD,
        };
        private readonly static Dictionary<uint, ItemType> ErLotItemTypes = new Dictionary<uint, ItemType>
        {
            [1] = ItemType.GOOD,
            [2] = ItemType.WEAPON,
            [3] = ItemType.ARMOR,
            [4] = ItemType.RING,
            [5] = ItemType.GEM,
            [6] = ItemType.EQUIP,
        };

        // echo $(ls | grep -E '_[1][0-2].msb') | sed -e 's/.msb[^ ]* /", "/g'
        // TODO: why not m60_45_36_10 edits
        private static readonly List<string> dupeMsbs = new List<string>
        {
            "m60_11_09_12", "m60_11_13_12",
            "m60_22_18_11", "m60_22_19_11", "m60_22_26_11", "m60_22_27_11",
            "m60_23_18_11", "m60_23_19_11", "m60_23_26_11", "m60_23_27_11",
            "m60_44_36_10", "m60_44_37_10", "m60_44_38_10", "m60_44_39_10",
            "m60_44_52_10", "m60_44_53_10", "m60_44_54_10", "m60_44_55_10",
            "m60_45_36_10", "m60_45_37_10", "m60_45_38_10", "m60_45_39_10",
            "m60_45_52_10", "m60_45_53_10", "m60_45_54_10", "m60_45_55_10",
            "m60_46_36_10", "m60_46_37_10", "m60_46_38_10", "m60_46_39_10",
            "m60_46_52_10", "m60_46_53_10", "m60_46_54_10", "m60_46_55_10",
            "m60_47_36_10", "m60_47_37_10", "m60_47_38_10", "m60_47_39_10",
            "m60_47_52_10", "m60_47_53_10", "m60_47_54_10", "m60_47_55_10",
        };
        private static readonly Regex MapRe = new Regex(@"m\d\d_\d\d_\d\d_\d\d");
        private Dictionary<string, string> MapDupes { get; set; }

        public Dictionary<string, string> Locations;
        public Dictionary<string, string> RevLocations;
        public Dictionary<string, string> LocationNames;
        public readonly Dictionary<uint, ItemType> LotItemTypes;
        public readonly Dictionary<ItemType, uint> LotValues;
        // Currently unused, as int/byte conversions with equipType are valid... currently.
        // TODO see if gem is sellable in Elden Ring.
        public readonly Dictionary<int, ItemType> ShopItemTypes = new Dictionary<int, ItemType>
        {
            [0] = ItemType.WEAPON,
            [1] = ItemType.ARMOR,
            [2] = ItemType.RING,
            [3] = ItemType.GOOD,
            [4] = ItemType.GEM,
        };

        /// <summary>
        /// The event ID to use for the next event returned by <see cref="GetUniqueEventId"/>.
        /// </summary>
        /// <remarks>
        /// <para>This is not necessarily always a valid event ID, it's just used to determine one
        /// later on.</para>
        /// <seealso cref="eventIdRanges"/>
        /// </remarks>
        // Event IDs are generally of the form `TMMMRIII`, where:
        //
        // * `T` is the "event type". Known types are 1 for miscellaneous EMEVD use, 5 to track
        //   item acquisition, 6 to track interactions with map objects, and 7 to track NPC
        //   interactions. 2 seems to work as well, but other digits are invalid.
        //
        // * `MMM` is the map ID. This is the first, second, and fourth digit in the EMEVD file
        //   names. For example, Undead Settlement is `m31_00_00_00.emevd` and its map ID is 310;
        //   Lothric Castle is `m30_01_00_00.emevd.js` and its map ID is 301. Only maps that are
        //   actually used by the game are valid, with the sole exception of 360 which seems to be
        //   cut content but remains usable. That's what we use here.
        //
        // * Any value of `R` is valid, but it has special semantics: if it's less than 5 (or 2 in
        //   Elden Ring), the event value is permanent. Otherwise, it resets to Off whenever the
        //   player dies, warps, quits out, or otherwise "resets" the world state.
        //
        // * `III` is just an ID, and any value is valid.
        //
        // "Invalid" events will do one of two things: either they'll set themselves to Off
        // shortly after being set to On (which can be useful behavior under some circumstances!)
        // or they'll mirror a valid event range, which is hard to detect but can wreak havoc on
        // game logic.
        //
        // TODO: find/verify unused ID ranges for ER and Sekiro
        private int nextEventId = 0;

        /// <summary>The ranges of valid custom event IDs for the randomizer to use.</summary>
        /// <remarks>
        /// <seealso cref="nextEventId"/>
        /// <seealso cref="GetUniqueEventId"/>
        /// </remarks>
        // TODO: find/verify unused ID ranges for ER and Sekiro
        private readonly List<Range> eventIdRanges = new(new[] {
            // This many ID slots is almost certainly overkill, but better safe than sorry.
            // These should remain sorted from lowest to highest.
            13600000..13605000,
            23600000..23605000,
            53600000..53605000,
            73600000..73605000
        });

        // Actual data
        private Dictionary<string, PARAM.Layout> Layouts = new Dictionary<string, PARAM.Layout>();
        private Dictionary<string, PARAMDEF> Defs = new Dictionary<string, PARAMDEF>();
        public ParamDictionary Params = new ParamDictionary();
        public Dictionary<string, IMsb> Maps = new Dictionary<string, IMsb>();
        public Dictionary<string, EMEVD> Emevds = new Dictionary<string, EMEVD>();
        public FMGDictionary ItemFMGs = new FMGDictionary();
        public FMGDictionary MenuFMGs = new FMGDictionary();
        public Dictionary<string, FMGDictionary> OtherItemFMGs = new Dictionary<string, FMGDictionary>();
        public Dictionary<string, FMGDictionary> OtherMenuFMGs = new Dictionary<string, FMGDictionary>();
        public Dictionary<string, Dictionary<string, ESD>> Talk = new Dictionary<string, Dictionary<string, ESD>>();

        // Lazily applies paramdefs
        public class ParamDictionary
        {
            public Dictionary<string, PARAM> Inner = new Dictionary<string, PARAM>();
            public Dictionary<string, PARAM.Layout> Layouts { get; set; }
            public Dictionary<string, PARAMDEF> Defs { get; set; }

            public PARAM this[string key]
            {
                get
                {
                    if (!Inner.TryGetValue(key, out PARAM param)) throw new Exception($"Internal error: Param {key} not found");
                    if (param.AppliedParamdef == null)
                    {
                        if (Defs != null && ApplyParamdefAggressively(param, Defs.Values))
                        {
                            // It worked
                        }
                        else if (Layouts != null && Layouts.TryGetValue(param.ParamType, out PARAM.Layout layout))
                        {
                            param.ApplyParamdef(layout.ToParamdef(param.ParamType, out _));
                        }
                        else throw new Exception($"Internal error: Param {key} has no def file");
                    }
                    return param;
                }
            }
            public bool ContainsKey(string key) => Inner.ContainsKey(key);
            public IEnumerable<string> Keys => Inner.Keys;
        }

        public static bool ApplyParamdefAggressively(PARAM param, IEnumerable<PARAMDEF> paramdefs)
        {
            foreach (PARAMDEF paramdef in paramdefs)
            {
                if (ApplyParamdefAggressively(param, paramdef))
                    return true;
            }
            return false;
        }

        private static bool ApplyParamdefAggressively(PARAM param, PARAMDEF paramdef)
        {
            // ApplyParamdefCarefully does not include enough info to diagnose failed cases.
            // For now, require that paramdef ParamType instances are unique, as there is no
            // naming convention for supporting multiple versions.
            if (param.ParamType == paramdef.ParamType)
            {
                if (param.ParamdefDataVersion == paramdef.DataVersion
                    && (param.DetectedSize == -1 || param.DetectedSize == paramdef.GetRowSize()))
                {
                    param.ApplyParamdef(paramdef);
                    return true;
                }
                else
                {
                    throw new Exception($"Error: {param.ParamType} cannot be applied (paramdef data version {paramdef.DataVersion} vs {param.ParamdefDataVersion}, paramdef size {paramdef.GetRowSize()} vs {param.DetectedSize})");
                }
            }
            return false;
        }

        // Lazily read FMGs
        // This could also be an IReadOnlyDictionary but it's ultimately still a randomizer-internal type
        public class FMGDictionary
        {
            public Dictionary<string, FMG> FMGs = new Dictionary<string, FMG>();
            public Dictionary<string, byte[]> Inner { get; set; }

            public FMG this[string key]
            {
                get
                {
                    if (!Inner.TryGetValue(key, out byte[] data)) throw new Exception($"Internal error: FMG {key} not found");
                    if (!FMGs.TryGetValue(key, out FMG fmg))
                    {
                        FMGs[key] = fmg = FMG.Read(data);
                    }
                    return fmg;
                }
            }
            public bool ContainsKey(string key) => Inner.ContainsKey(key);
            public IEnumerable<string> Keys => Inner.Keys;
        }

        // Names
        public SortedDictionary<ItemKey, string> ItemNames = new SortedDictionary<ItemKey, string>();
        public SortedDictionary<string, List<ItemKey>> RevItemNames = new SortedDictionary<string, List<ItemKey>>();

        private SortedDictionary<int, string> qwcNames = new SortedDictionary<int, string>();
        private SortedDictionary<int, string> lotNames = new SortedDictionary<int, string>();
        private SortedDictionary<int, string> characterSplits = new SortedDictionary<int, string>();
        private SortedDictionary<string, string> modelNames = new SortedDictionary<string, string>();

        private List<string> writtenFiles = new List<string>();

        public GameData(string dir, FromGame game)
        {
            Dir = dir;
            Editor = new GameEditor(game);
            Editor.Spec.GameDir = $@"{dir}";
            Editor.Spec.NameDir = $@"{dir}\Names";
            if (EldenRing)
            {
                Editor.Spec.DefDir = $@"{dir}\Defs";
                // Editor.Spec.DefDir = $@"..\ParamdexNew\ER\Defs";
            }
            else
            {
                Editor.Spec.LayoutDir = $@"{dir}\Layouts";
            }
            LotItemTypes = EldenRing ? ErLotItemTypes : MaskLotItemTypes;
            LotValues = LotItemTypes.ToDictionary(e => e.Value, e => e.Key);
        }

        // The IMsb interface is not usable directly, so in lieu of making GameData extremely generic, add these casts
        public Dictionary<string, MSB3> DS3Maps => Maps.ToDictionary(e => e.Key, e => e.Value as MSB3);
        public Dictionary<string, MSBS> SekiroMaps => Maps.ToDictionary(e => e.Key, e => e.Value as MSBS);
        public Dictionary<string, MSBE> EldenMaps =>
            Maps.Where(e => e.Value is MSBE).ToDictionary(e => e.Key, e => e.Value as MSBE);

        public void Load(string modDir = null)
        {
            ModDir = modDir;
            LoadNames();
            LoadParams();
            LoadMapData();
            LoadTalk();
            LoadScripts();
            LoadText();
        }

        public void UnDcx(string dir)
        {
            Directory.CreateDirectory($@"{dir}\dcx");
            foreach (string path in Directory.GetFiles(dir, "*.dcx"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                byte[] f = DCX.Decompress(path).ToArray();
                File.WriteAllBytes($@"{dir}\dcx\{name}", f);
            }
        }

        public void ReDcx(string dir, string ext)
        {
            foreach (string path in Directory.GetFiles($@"{dir}\dcx", "*." + ext))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                DCX.Compress(File.ReadAllBytes(path), (DCX.Type)DCX.DefaultType.Sekiro, $@"{dir}\{name}.{ext}.dcx");
            }
        }

        public PARAM Param(string name)
        {
            return Params[name];
        }

        public PARAM Param(ItemType type)
        {
            if (type == ItemType.EQUIP) return null;
            return Params[itemParams[(int) type]];
        }

        public PARAM.Row Item(ItemKey key)
        {
            if (!Sekiro) key = NormalizeWeapon(key);
            if (key.Type == ItemType.EQUIP) return null;
            return Param(key.Type)[key.ID];
        }

        public PARAM.Row AddRow(string name, int id, int oldId = -1)
        {
            PARAM param = Params[name];
            if (param[id] != null)
            {
                // This can get quadratic? But eh good to check
                throw new Exception($"Trying to add id {id} in {name} but already exists");
            }
            PARAM.Row row = new PARAM.Row(id, "", param.AppliedParamdef);
            param.Rows.Add(row);
            if (oldId >= 0)
            {
                GameEditor.CopyRow(param[oldId], row);
            }
            return row;
        }

        private static ItemKey NormalizeWeapon(ItemKey key)
        {
            // Maybe can put this logic in ItemKey itself
            if (key.Type == ItemType.WEAPON && key.ID % 100 != 0)
            {
                return new ItemKey(key.Type, key.ID - (key.ID % 100));
            }
            return key;
        }

        public string Name(ItemKey key)
        {
            string suffix = "";
            if (key.Type == ItemType.WEAPON && key.ID % 100 != 0)
            {
                suffix = $" +{key.ID % 100}";
                key = new ItemKey(key.Type, key.ID - (key.ID % 100));
            }
            // suffix += $" {key.ID}";
            return (ItemNames.ContainsKey(key) ? ItemNames[key] : $"?ITEM?" + $" ({(int)key.Type}:{key.ID})") + suffix;
        }

        private static readonly Dictionary<ItemKey, string> customNamesDS3 = new Dictionary<ItemKey, string>
        {
            { new ItemKey(ItemType.GOOD, 2123), "Cinders of a Lord (Abyss Watchers)" },
            { new ItemKey(ItemType.GOOD, 2124), "Cinders of a Lord (Aldrich)" },
            { new ItemKey(ItemType.GOOD, 2125), "Cinders of a Lord (Yhorm)" },
            { new ItemKey(ItemType.GOOD, 2126), "Cinders of a Lord (Lothric)" },
        };
        private static readonly Dictionary<ItemKey, string> customNamesElden = new Dictionary<ItemKey, string>
        {
            { new ItemKey(ItemType.GOOD, 8127), "Letter from Volcano Manor (Istvan)" },
            { new ItemKey(ItemType.GOOD, 8132), "Letter from Volcano Manor (Rileigh)" },
            { new ItemKey(ItemType.GOOD, 8174), "Academy Glintstone Key (Thops)" },
            { new ItemKey(ItemType.GOOD, 8196), "Unalloyed Gold Needle (Milicent)" },
            { new ItemKey(ItemType.GOOD, 8975), "Unalloyed Gold Needle (Broken)" },
            { new ItemKey(ItemType.GOOD, 8976), "Unalloyed Gold Needle (Fixed)" },
        };

        public string DisplayName(ItemKey key, int quantity = 1)
        {
            if (DS3 && customNamesDS3.TryGetValue(key, out string name))
            {
                return name;
            }
            if (EldenRing && customNamesElden.TryGetValue(key, out name))
            {
                return name;
            }
            if (key.Type == ItemType.EQUIP)
            {
                return "NPC Equipment";
            }
            string quantityStr = quantity <= 1 ? "" : $" {quantity}x";
            return Name(key) + quantityStr;
        }

        public ItemKey ItemForName(string name)
        {
            if (!RevItemNames.ContainsKey(name)) throw new Exception($"Internal error: missing name {name}");
            if (RevItemNames[name].Count != 1) throw new Exception($"Internal error: ambiguous name {name} could be {string.Join(" or ", RevItemNames[name])}");
            return RevItemNames[name][0];
        }

        public SortedDictionary<ItemKey, string> Names()
        {
            return ItemNames;
        }

        public string LotName(int id)
        {
            return lotNames.ContainsKey(id) ? lotNames[id] : "?LOT?";
        }

        public string QwcName(int id)
        {
            return qwcNames.ContainsKey(id) ? qwcNames[id] : $"after {id}";
        }

        public string CharacterName(int id)
        {
            if (EldenRing)
            {
                return characterSplits.TryGetValue(id, out string n) ? n : null;
            }
            int chType = 0;
            foreach (KeyValuePair<int, string> entry in characterSplits)
            {
                if (entry.Key > id)
                {
                    break;
                }
                chType = entry.Key;
            }
            string name = characterSplits[chType];
            return name == "UNUSED" ? null : name;
        }

        public string ModelName(string chr)
        {
            return modelNames.TryGetValue(chr, out string m) ? m : chr;
        }

        public string ModelCharacterName(string chr, int id)
        {
            return id > 0 ? (CharacterName(id) ?? ModelName(chr)) : ModelName(chr);
        }

        public List<string> GetModelNames()
        {
            return modelNames.Values.ToList();
        }

        public static bool ExtractModelName(string name, out string modelName)
        {
            modelName = null;
            int split = name.LastIndexOf('_');
            if (split == -1)
            {
                return false;
            }
            modelName = name.Substring(0, split);
            // Elden Ring cross-map names
            if (modelName.StartsWith("m") && modelName.Contains('-'))
            {
                modelName = modelName.Split('-')[1];
            }
            return true;
        }

        public string EntityName(EntityId entity, bool detail = false, bool mapName = false)
        {
            string mapSuffix = mapName && !string.IsNullOrEmpty(entity.MapName)
                ? " in " + MapLocationName(entity.MapName, entity.OriginalMapName)
                : "";
            if (!ExtractModelName(entity.EntityName, out string model))
            {
                return entity.EntityName + mapSuffix;
            }
            string modelName = model;
            if (modelName == "c0000")
            {
                modelName = CharacterName(entity.CharaInitID) ?? (EldenRing ? $"Human {entity.CharaInitID}" : "c0000");
            }
            if (modelNames.ContainsKey(modelName))
            {
                modelName = modelNames[modelName];
            }
            if (!detail)
            {
                // Note this doesn't do a CharacterName override, so using sparingly, or fix this
                return modelName + mapSuffix;
            }
            List<string> details = new List<string>();
            if (modelName != model)
            {
                details.Add(modelName);
            }
            if (entity.EntityID > 0)
            {
                details.Add($"id {entity.EntityID}");
            }
            if (entity.GroupIds != null && entity.GroupIds.Count > 0)
            {
                details.Add($"group {string.Join(",", entity.GroupIds)}");
            }
            if (entity.NameID > 0)
            {
                string fmgName = ItemFMGs["NpcName"][entity.NameID];
                if (!string.IsNullOrEmpty(fmgName))
                {
                    details.Add($"<{fmgName}>");
                }
            }
            return (entity.Type == null ? "" : $"{entity.Type} ")
                + entity.EntityName
                + (details.Count > 0 ? $" ({string.Join(" - ", details)})" : "")
                + mapSuffix;
        }

        public string MapLocationName(string mapId, string lowLevelMapId = null)
        {
            return $"{lowLevelMapId ?? mapId}" + (LocationNames.TryGetValue(mapId, out string mapName) ? $" ({mapName})" : "");
        }

        // Map name utility functions
        // Especially in Elden Ring, maps are stored in param field bytes
        public static List<byte> ParseMap(string map) => map.TrimStart('m').Split('_').Select(p => byte.Parse(p)).ToList();

        public static string FormatMap(IEnumerable<byte> bytes)
        {
            return "m" + string.Join("_", bytes.Select(b => b == 0xFF ? "XX" : $"{b:d2}"));
        }

        private readonly List<string> ParamMapIdFields = new List<string> { "areaNo", "gridXNo", "gridZNo" };
        public List<byte> GetMapParts(PARAM.Row row, List<string> fields = null)
        {
            if (fields == null) fields = ParamMapIdFields;
            List<byte> bytes = fields.Select(f => (byte)row[f].Value).ToList();
            while (bytes.Count < 4) bytes.Add(0);
            return bytes;
        }

        public HashSet<string> GetEldenFrameMaps()
        {
            // TODO: General system with FLVER reading, maybe
            HashSet<string> eldenFrameMaps = new HashSet<string>
            {
                // Mainly academy and redmane have confirmed issues
                "m10_00_00_00", "m12_05_00_00", "m14_00_00_00", "m15_00_00_00", "m16_00_00_00",
                "m18_00_00_00", "m35_00_00_00",
                "m60_39_54_00", // Shaded Castle
                "m60_43_31_00", // Morne
                "m60_51_36_00", // Redmane
                "m60_51_57_00", // Sol
                "m60_46_36_00", // Haight
                "m60_51_39_00", // Faroth
            };
            // Plus all side-dungeons, m30 m31 m32 m34
            // Octopus: 2.4. Lobster: 4.4. Crab: 1.9.
            // If >2.5, allowframes
            Regex tightRe = new Regex(@"^m3[0-4]");
            eldenFrameMaps.UnionWith(Maps.Keys.Where(m => tightRe.IsMatch(m)));
            return eldenFrameMaps;
        }

        public void SaveSekiro(string outPath)
        {
            Console.WriteLine("Writing to " + outPath);
            writtenFiles.Clear();

            foreach (KeyValuePair<string, IMsb> entry in Maps)
            {
                if (!Locations.ContainsKey(entry.Key)) continue;
                string path = $@"{outPath}\map\mapstudio\{entry.Key}.msb.dcx";
                AddModFile(path);
                entry.Value.Write(path, (DCX.Type)DCX.DefaultType.Sekiro);
            }
            foreach (KeyValuePair<string, Dictionary<string, ESD>> entry in Talk)
            {
                if (!Locations.ContainsKey(entry.Key) && entry.Key != "m00_00_00_00") continue;
                WriteModDependentBnd(outPath, $@"{Dir}\Base\{entry.Key}.talkesdbnd.dcx", $@"script\talk\{entry.Key}.talkesdbnd.dcx", entry.Value);
            }
            foreach (KeyValuePair<string, EMEVD> entry in Emevds)
            {
                string path = $@"{outPath}\event\{entry.Key}.emevd.dcx";
                AddModFile(path);
                entry.Value.Write(path, (DCX.Type)DCX.DefaultType.Sekiro);
#if DEBUG
                string scriptFile = path + ".js";
                if (File.Exists(scriptFile))
                {
                    Console.WriteLine($"Deleting {scriptFile}");
                    File.Delete(scriptFile);
                }
#endif
            }

            WriteModDependentBnd(outPath, $@"{Dir}\Base\gameparam.parambnd.dcx", $@"param\gameparam\gameparam.parambnd.dcx", Params.Inner);
            WriteModDependentBnd(outPath, $@"{Dir}\Base\item.msgbnd.dcx", $@"msg\engus\item.msgbnd.dcx", ItemFMGs.FMGs);
            WriteModDependentBnd(outPath, $@"{Dir}\Base\menu.msgbnd.dcx", $@"msg\engus\menu.msgbnd.dcx", MenuFMGs.FMGs);
            foreach (KeyValuePair<string, FMGDictionary> entry in OtherItemFMGs)
            {
                WriteModDependentBnd(outPath, $@"{Dir}\Base\msg\{entry.Key}\item.msgbnd.dcx", $@"msg\{entry.Key}\item.msgbnd.dcx", entry.Value.FMGs);
            }

            MergeMods(outPath);
            Console.WriteLine("Success!");
        }

        public static void RestoreBackupsInternal(string outPath)
        {
            Console.WriteLine("Restoring from " + outPath);

            foreach (string bakPath in GetBackupFiles(outPath))
            {
                string dest = GetRestoreName(bakPath);
                Console.WriteLine($"Restoring {dest}");
                RestoreBackup(bakPath);
            }
        }

        public static List<string> GetBackupFiles(string outPath)
        {
            List<string> restoreDirs = new List<string>
            {
                $@"{outPath}",
                $@"{outPath}\event",
                $@"{outPath}\script\talk",
                $@"{outPath}\map\mapstudio",
            };
            restoreDirs.AddRange(MiscSetup.Langs.Keys.Select(lang => $@"{outPath}\msg\{lang}"));
            List<string> backups = new List<string>();
            foreach (string restoreDir in restoreDirs)
            {
                if (Directory.Exists(restoreDir))
                {
                    backups.AddRange(Directory.GetFiles(restoreDir, "*.randobak"));
                }
            }
            return backups;
        }

        public static string GetRestoreName(string bakPath)
        {
            if (!bakPath.EndsWith(".randobak")) throw new Exception($"Cannot restore {bakPath}, must end in .randobak");
            return bakPath.Substring(0, bakPath.Length - ".randobak".Length);
        }

        public static void RestoreBackup(string bakPath)
        {
            string dest = GetRestoreName(bakPath);
            if (!File.Exists(dest))
            {
                // This will generally only happen if the user deleted it.
                // We can warn that we're restoring it? idk
                // Console.WriteLine($"Warning: {dest} does not exist");
            }
            else
            {
                File.Delete(dest);
            }
            File.Move(bakPath, dest);
        }

        public HashSet<string> WriteEmevds = new HashSet<string>();
        public HashSet<string> WriteESDs = new HashSet<string>();
        public HashSet<string> WriteMSBs = new HashSet<string>();
        public bool WriteFMGs = false;
        public void SaveEldenRing(string outPath, bool uxm, string optionsStr, Action<double> notify = null)
        {
            Console.WriteLine("Writing to " + outPath);
            RuntimeParamChecker checker = new RuntimeParamChecker();
            checker.ScanMaps(Maps);
            checker.CheckEntries(this);
            // Sorry TK, Oodle is 2slow
            DCX.Type overrideDcx = DCX.Type.DCX_DFLT_11000_44_9;
            byte[] optionsByte = Encoding.ASCII.GetBytes(optionsStr);
            // overrideDcx = DCX.Type.DCX_KRAK;
            writtenFiles.Clear();
            {
                string basePath = $@"{Dir}\Vanilla\regulation.bin";
                string path = $@"{outPath}\regulation.bin";
                if (ModDir != null)
                {
                    string modPath = $@"{ModDir}\regulation.bin";
                    if (File.Exists(modPath)) basePath = modPath;
                }
                AddModFile(path);
                if (uxm) Backup(path);
                // Hack to add options string to params
                List<int> textIds = Enumerable.Range(777777771, 4).ToList();
                Params["CutSceneTextureLoadParam"].Rows.RemoveAll(r => textIds.Contains(r.ID));
                int offset = 0;
                foreach (int textId in textIds)
                {
                    PARAM.Row row = AddRow("CutSceneTextureLoadParam", textId, 0);
                    for (int i = 0; i < 16; i++)
                    {
                        int remaining = optionsByte.Length - offset;
                        if (remaining <= 0) break;
                        row[$"texName_{i:d2}"].Value = Encoding.ASCII.GetString(optionsByte, offset, Math.Min(remaining, 16));
                        offset += 16;
                    }
                }
                Editor.OverrideBndRel(basePath, path, Params.Inner, f => f.AppliedParamdef == null ? null : f.Write(), dcx: overrideDcx);
            }

            HashSet<string> dupedPaths = new HashSet<string>();
            string getDupePath(ICollection<string> writeMaps, string map, string path)
            {
                MapDupes.TryGetValue(map, out string dupe);
                // If map is written and dupe also is, write nothing
                // If map is written and dupe is not, write dupe
                // If map is not written and dupe is, do nothing
                // If map is not written and dupe is not, delete/restore dupe
                // tl;dr if dupe is written, do nothing
                if (dupe == null || writeMaps.Contains(dupe)) return null;
                string dupePath = path.Replace(map, dupe);
                if (path == dupePath) throw new Exception($"Internal error: identical duplicate {path}");
                dupedPaths.Add(dupePath);
                return dupePath;
            }

            // Event scripts
            foreach (KeyValuePair<string, EMEVD> entry in Emevds)
            {
                string map = entry.Key;
                string path = $@"{outPath}\event\{map}.emevd.dcx";
                if (dupedPaths.Contains(path)) continue;
                string dupePath = getDupePath(WriteEmevds, map, path);
                bool write = WriteEmevds.Contains(map);
                AddBackupOrRestoreFile(path, write, uxm);
                if (dupePath != null)
                {
                    AddBackupOrRestoreFile(dupePath, write, uxm);
                }
                if (!write) continue;
#if !DEBUG
                checker.EditEvents(map, entry.Value);
#endif
                entry.Value.StringData = entry.Value.StringData.Concat(new byte[] { 0 }).Concat(optionsByte).ToArray();
                entry.Value.Write(path, overrideDcx);
                if (dupePath != null)
                {
                    File.Copy(path, dupePath, true);
                }
#if DEBUG
                string scriptFile = path + ".js";
                if (File.Exists(scriptFile))
                {
                    Console.WriteLine($"Deleting {scriptFile}");
                    File.Delete(scriptFile);
                }
#endif
            }
#if DEBUG
            Console.WriteLine("Wrote event scripts");
#endif

            // Hoarah Loux standalone SFX
            {
                string path = $@"{outPath}\sfx\sfxbnd_c4721.ffxbnd.dcx";
                string basePath = $@"{Dir}\Vanilla\sfxbnd_c4720.ffxbnd.dcx";
                if (File.Exists(basePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(basePath, path, true);
                }
            }

            void updateFmg(string lang, string type, FMGDictionary fmgs)
            {
                string path = $@"{outPath}\msg\{lang}\{type}.msgbnd.dcx";
                AddBackupOrRestoreFile(path, WriteFMGs, uxm);
                if (WriteFMGs)
                {
                    string basePath = $@"{Dir}\Vanilla\msg\{lang}\{type}.msgbnd.dcx";
                    if (ModDir != null)
                    {
                        string modPath = $@"{ModDir}\msg\{lang}\{type}.msgbnd.dcx";
                        if (File.Exists(modPath)) basePath = modPath;
                    }
                    Editor.OverrideBndRel(basePath, path, fmgs.FMGs, f => f.Write(), dcx: overrideDcx);
                }
            }

            // Text
            // Early on (as modengine can't reload it), but after events
            {
                // Just menu for now, and no override for now. Also other languages
                updateFmg("engus", "menu", MenuFMGs);
                foreach (KeyValuePair<string, FMGDictionary> entry in OtherMenuFMGs)
                {
                    updateFmg(entry.Key, "menu", entry.Value);
                }
                updateFmg("engus", "item", ItemFMGs);
                foreach (KeyValuePair<string, FMGDictionary> entry in OtherItemFMGs)
                {
                    updateFmg(entry.Key, "item", entry.Value);
                }
            }

            // ESDs
            foreach (KeyValuePair<string, Dictionary<string, ESD>> entry in Talk)
            {
                string path = $@"{outPath}\script\talk\{entry.Key}.talkesdbnd.dcx";
                bool write = WriteESDs.Contains(entry.Key);
                AddBackupOrRestoreFile(path, write, uxm);
                if (!write) continue;
                string basePath = $@"{Dir}\Vanilla\{entry.Key}.talkesdbnd.dcx";
                if (ModDir != null)
                {
                    string modPath = $@"{ModDir}\script\talk\{entry.Key}.talkesdbnd.dcx";
                    if (File.Exists(modPath)) basePath = modPath;
                }
                Editor.OverrideBndRel(basePath, path, entry.Value, f => f.Write(), dcx: overrideDcx);
            }

            // Maps
            int count = 0;
            foreach (KeyValuePair<string, IMsb> entry in Maps)
            {
                notify?.Invoke((double)count++ / Maps.Count);
                string map = entry.Key;
                string path = $@"{outPath}\map\mapstudio\{map}.msb.dcx";
                if (dupedPaths.Contains(path)) continue;
                string dupePath = getDupePath(WriteMSBs, map, path);
                bool write = WriteMSBs.Contains(map);
                AddBackupOrRestoreFile(path, write, uxm);
                if (dupePath != null)
                {
                    AddBackupOrRestoreFile(dupePath, write, uxm);
                }
                if (!write) continue;
                if (entry.Value is MSBE msb)
                {
                    msb.Events.Navmeshes.Add(new MSBE.Event.Navmesh
                    {
                        Name = optionsStr,
                        NavmeshRegionName = null,
                    });
                }
                entry.Value.Write(path, overrideDcx);
                // entry.Value.Write(path.Replace(".dcx", ""), DCX.Type.None);
                if (dupePath != null)
                {
                    File.Copy(path, dupePath, true);
                }
            }
            if (Maps.Count > 0) notify?.Invoke(1);
        }

        private static string Backup(string file)
        {
            string bak = file + ".randobak";
            if (!File.Exists(bak))
            {
                File.Copy(file, bak, false);
            }
            return bak;
        }

        private void AddBackupOrRestoreFile(string path, bool write, bool uxm)
        {
            if (write)
            {
                AddModFile(path);
                if (uxm) Backup(path);
            }
            else if (uxm)
            {
                string bak = path + ".randobak";
                if (File.Exists(bak))
                {
                    RestoreBackup(bak);
                }
            }
            else if (File.Exists(path))
            {
                Console.WriteLine($"Deleting {path}");
                File.Delete(path);
            }
        }

        public void SaveDS3(string outPath, bool encrypted)
        {
            Console.WriteLine("Writing to " + outPath);
            writtenFiles.Clear();

            // Maps
            foreach (KeyValuePair<string, IMsb> entry in Maps)
            {
                if (!Locations.ContainsKey(entry.Key)) continue;
                string path = $@"{outPath}\map\mapstudio\{entry.Key}.msb.dcx";
                AddModFile(path);
                entry.Value.Write(path);
            }

            // Save params
            // This is complicated enough (and probably also a bit wrong) such that WriteModDependentBnd is too simple.
            {
                string basePath = $@"{Dir}\Base\Data0.bdt";
                if (ModDir != null)
                {
                    string modPath1 = $@"{ModDir}\param\gameparam\gameparam.parambnd.dcx";
                    string modPath2 = $@"{ModDir}\Data0.bdt";
                    if (File.Exists(modPath1)) basePath = modPath1;
                    else if (File.Exists(modPath2)) basePath = modPath2;
                }
                string path = encrypted ? $@"{outPath}\Data0.bdt" : $@"{outPath}\param\gameparam\gameparam.parambnd.dcx";
                AddModFile(path);
                Editor.OverrideBndRel(basePath, path, Params.Inner, f => f.Write());
            }

            // Messages
            WriteModDependentBnd(outPath, $@"{Dir}\Base\msg\engus\item_dlc2.msgbnd.dcx", $@"msg\engus\item_dlc2.msgbnd.dcx", ItemFMGs.FMGs);
            foreach (KeyValuePair<string, FMGDictionary> entry in OtherItemFMGs)
            {
                WriteModDependentBnd(outPath, $@"{Dir}\Base\msg\{entry.Key}\item_dlc2.msgbnd.dcx", $@"msg\{entry.Key}\item_dlc2.msgbnd.dcx", entry.Value.FMGs);
            }

            // Event scripts
            foreach (KeyValuePair<string, EMEVD> entry in Emevds)
            {
                string path = $@"{outPath}\event\{entry.Key}.emevd.dcx";
                AddModFile(path);
                entry.Value.Write(path);
#if DEBUG
                string scriptFile = path + ".js";
                if (File.Exists(scriptFile))
                {
                    Console.WriteLine($"Deleting {scriptFile}");
                    File.Delete(scriptFile);
                }
#endif
            }

            MergeMods(outPath);
            Console.WriteLine("Success!");
        }

        private static string FullName(string path)
        {
            return new FileInfo(path).FullName;
        }

        private void AddModFile(string path)
        {
            path = FullName(path);
            bool suppress = false;
#if DEBUG
            suppress = true;
#endif
            if (!suppress) Console.WriteLine($"Writing {path}");
            writtenFiles.Add(path);
        }

        void WriteModDependentBnd<T>(string outPath, string basePath, string relOutputPath, Dictionary<string, T> diffData)
            where T : SoulsFile<T>, new()
        {
            if (ModDir != null)
            {
                string modPath = $@"{ModDir}\{relOutputPath}";
                if (File.Exists(modPath)) basePath = modPath;
            }
            string path = $@"{outPath}\{relOutputPath}";
            AddModFile(path);
            Editor.OverrideBnd(basePath, Path.GetDirectoryName(path), diffData, f => f.Write());
        }

        private void MergeMods(string outPath)
        {
            Console.WriteLine("Processing extra mod files...");
            bool work = false;
            if (ModDir != null)
            {
                foreach (string gameFile in MiscSetup.GetGameFiles(ModDir, Sekiro))
                {
                    string source = FullName($@"{ModDir}\{gameFile}");
                    string target = FullName($@"{outPath}\{gameFile}");
                    if (writtenFiles.Contains(target)) continue;
                    Console.WriteLine($"Copying {source}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, true);
                    writtenFiles.Add(target);
                    work = true;
                }
            }
            foreach (string gameFile in MiscSetup.GetGameFiles(outPath, Sekiro))
            {
                string target = FullName($@"{outPath}\{gameFile}");
                if (writtenFiles.Contains(target)) continue;
                Console.WriteLine($"Found extra file (delete it if you don't want it): {target}");
                work = true;
            }
            if (!work) Console.WriteLine("No extra files found");
        }

        private void LoadNames()
        {
            modelNames = new SortedDictionary<string, string>(Editor.LoadNames("ModelName", n => n, false));
            characterSplits = new SortedDictionary<int, string>(Editor.LoadNames("CharaInitParam", n => int.Parse(n), true));
            lotNames = new SortedDictionary<int, string>(Editor.LoadNames("ItemLotParam", n => int.Parse(n), true));
            qwcNames = new SortedDictionary<int, string>(Editor.LoadNames("ShopQwc", n => int.Parse(n), true));
            for (int i = 0; i < itemParams.Count; i++)
            {
                if (!EldenRing && itemParams[i] == "EquipParamGem") continue;
                foreach (KeyValuePair<ItemKey, string> entry in Editor.LoadNames(itemParams[i], n => new ItemKey((ItemType)i, int.Parse(n)), true))
                {
                    ItemNames[entry.Key] = entry.Value;
                    AddMulti(RevItemNames, entry.Value, entry.Key);
                }
            }
            if (characterSplits.Count == 0)
            {
                characterSplits[0] = "UNUSED";
            }
            if (EldenRing)
            {
                LocationNames = Editor.LoadNames("MapName", n => n, false);
                // For now, don't have special location names, but we can maybe do this for legacy dungeons or have prefixes
                Locations = LocationNames.ToDictionary(e => e.Key, e => e.Key);
            }
            else if (DS3)
            {
                LocationNames = DS3MapNames;
                Locations = DS3LocationNames;
            }
            else if (Sekiro)
            {
                LocationNames = SekiroMapNames;
                Locations = SekiroLocationNames;
            }
            RevLocations = Locations.ToDictionary(e => e.Value, e => e.Key);
        }

        // https://github.com/JKAnderson/Yapped/blob/master/Yapped/FormMain.cs
        private void LoadLayouts()
        {
            if (Editor.Spec.LayoutDir == null)
            {
                Defs = Editor.LoadDefs();
            }
            else
            {
                Layouts = Editor.LoadLayouts();
            }
        }

        private void LoadParams()
        {
            bool lazy = true;
            Dictionary<string, PARAM> dict;
            string path;
            if (!lazy)
            {
                // Delay loading layouts if we'll do it in ParamDictionary
                LoadLayouts();
            }
            if (DS3)
            {
                path = $@"{Dir}\Base\Data0.bdt";
                string modPath1 = $@"{ModDir}\param\gameparam\gameparam.parambnd.dcx";
                string modPath2 = $@"{ModDir}\Data0.bdt";
                if (ModDir != null && File.Exists(modPath1))
                {
                    Console.WriteLine($"Using modded file {modPath1}");
                    path = modPath1;
                }
                else if (ModDir != null && File.Exists(modPath2))
                {
                    Console.WriteLine($"Using modded file {modPath2}");
                    path = modPath2;
                }
                if (!File.Exists(path))
                {
                    throw new Exception($"Missing param file: {path}");
                }
                dict = Editor.LoadParams(path, layouts: lazy ? null : Layouts, allowError: true);
            }
            else if (Sekiro)
            {
                path = $@"{Dir}\Base\gameparam.parambnd.dcx";
                string modPath = $@"{ModDir}\param\gameparam\gameparam.parambnd.dcx";
                if (ModDir != null && File.Exists(modPath))
                {
                    Console.WriteLine($"Using modded file {modPath}");
                    path = modPath;
                }
                if (!File.Exists(path))
                {
                    throw new Exception($"Missing param file: {path}");
                }
                dict = Editor.LoadParams(path, layouts: Layouts, allowError: false);
            }
            else if (EldenRing)
            {
                path = $@"{Dir}\Vanilla\regulation.bin";
                string modPath = $@"{ModDir}\regulation.bin";
                if (ModDir != null && File.Exists(modPath))
                {
                    Console.WriteLine($"Using modded file {modPath}");
                    path = modPath;
                }
                if (!File.Exists(path))
                {
                    throw new Exception($"Missing param file: {path}");
                }
                dict = Editor.LoadParams(path, defs: Defs);
            }
            else throw new Exception();
            if (lazy)
            {
                LoadLayouts();
            }
            Params = new ParamDictionary
            {
                Inner = dict,
                Layouts = Layouts,
                Defs = Defs,
            };
        }

        private void LoadMapData()
        {
            if (Sekiro)
            {
                Maps = Editor.Load("Base", path => (IMsb)MSBS.Read(path), "*.msb.dcx");
                MaybeOverrideFromModDir(Maps, name => $@"map\MapStudio\{name}.msb.dcx", path => MSBS.Read(path));
                List<string> missing = Locations.Keys.Except(Maps.Keys).ToList();
                if (missing.Count != 0) throw new Exception($@"Missing msbs in dists\Base: {string.Join(", ", missing)}");
            }
            else if (DS3)
            {
                Maps = Editor.Load("Base", path => (IMsb)MSB3.Read(path), "*.msb.dcx");
                MaybeOverrideFromModDir(Maps, name => $@"map\MapStudio\{name}.msb.dcx", path => MSB3.Read(path));
                List<string> missing = Locations.Keys.Except(Maps.Keys).ToList();
                if (missing.Count != 0) throw new Exception($@"Missing msbs in dist\Base: {string.Join(", ", missing)}");
            }
            else
            {
                Maps = Editor.Load("Vanilla", path => (IMsb)MSBE.Read(path), "*.msb.dcx");
                MaybeOverrideFromModDir(Maps, name => $@"map\MapStudio\{name}.msb.dcx", path => MSBE.Read(path));

                // Set up copying dupe maps which are not included in our vanilla files
                Regex lastRe = new Regex(@"_1([0-2])$");
                MapDupes = dupeMsbs
                    .Where(m => !Maps.ContainsKey(m))
                    .ToDictionary(m => lastRe.Replace(m, @"_0$1"), m => m);
                if (MapDupes.Any(e => e.Key == e.Value))
                {
                    throw new Exception($"Invalid dupe map {string.Join(" ", MapDupes)}");
                }
            }
        }

        private void LoadTalk()
        {
            if (!DS3)
            {
                Talk = Editor.LoadBnds(EldenRing ? "Vanilla" : "Base", (data, path) => ESD.Read(data), "*.talkesdbnd.dcx");
                MaybeOverrideFromModDir(Talk, name => $@"script\talk\{name}.talkesdbnd.dcx", path => Editor.LoadBnd(path, (data, path2) => ESD.Read(data)));
                if (Sekiro)
                {
                    List<string> missing = Locations.Keys.Concat(new[] { "m00_00_00_00" }).Except(Talk.Keys).ToList();
                    if (missing.Count != 0) throw new Exception($@"Missing talkesdbnds in dist\Base: {string.Join(", ", missing)}");
                }
            }
        }

        private void LoadScripts()
        {
            Emevds = Editor.Load(EldenRing ? "Vanilla" : "Base", path => EMEVD.Read(path), "*.emevd.dcx");
            MaybeOverrideFromModDir(Emevds, name => $@"event\{name}.emevd.dcx", path => EMEVD.Read(path));
            if (!EldenRing)
            {
                List<string> missing = Locations.Keys.Concat(new[] { "common", "common_func" }).Except(Emevds.Keys).ToList();
                if (missing.Count != 0) throw new Exception($@"Missing emevds in dist\Base: {string.Join(", ", missing)}");
            }
            if (EldenRing && false)
            {
                EMEVD template = Emevds.Where(e => e.Key.StartsWith("m")).Select(e => e.Value).FirstOrDefault();
                if (template == null) throw new Exception(@"Missing emevds in diste\Vanilla");
                // TODO: For this to work, we'd need to modify the loadlist, unfortunately
                foreach (string map in Maps.Keys)
                {
                    if (!Emevds.ContainsKey(map))
                    {
                        EMEVD emevd = new EMEVD
                        {
                            Format = template.Format,
                            Compression = template.Compression,
                            StringData = template.StringData,
                        };
                        Emevds[map] = emevd;
                    }
                }
            }
        }

        private void LoadText()
        {
            // TODO: Surely we can merge these
            FMGDictionary read(string path)
            {
                Dictionary<string, byte[]> fmgBytes = Editor.LoadBnd(path, (data, _) => data);
                return new FMGDictionary { Inner = fmgBytes };
            }
            if (Sekiro)
            {
                ItemFMGs = read($@"{Dir}\Base\item.msgbnd.dcx");
                ItemFMGs = MaybeOverrideFromModDir(ItemFMGs, @"msg\engus\item.msgbnd.dcx", read);
                MenuFMGs = read($@"{Dir}\Base\menu.msgbnd.dcx");
                MenuFMGs = MaybeOverrideFromModDir(MenuFMGs, @"msg\engus\menu.msgbnd.dcx", read);
                foreach (string lang in MiscSetup.Langs.Keys)
                {
                    if (lang == "engus") continue;
                    OtherItemFMGs[lang] = read($@"{Dir}\Base\msg\{lang}\item.msgbnd.dcx");
                    OtherItemFMGs[lang] = MaybeOverrideFromModDir(OtherItemFMGs[lang], $@"msg\{lang}\item.msgbnd.dcx", read);
                }
            }
            else if (DS3)
            {
                ItemFMGs = read($@"{Dir}\Base\msg\engus\item_dlc2.msgbnd.dcx");
                ItemFMGs = MaybeOverrideFromModDir(ItemFMGs, @"msg\engus\item_dlc2.msgbnd.dcx", read);
                foreach (string lang in MiscSetup.Langs.Keys)
                {
                    if (lang == "engus" || MiscSetup.NoDS3Langs.Contains(lang)) continue;
                    OtherItemFMGs[lang] = read($@"{Dir}\Base\msg\{lang}\item_dlc2.msgbnd.dcx");
                    OtherItemFMGs[lang] = MaybeOverrideFromModDir(OtherItemFMGs[lang], $@"msg\{lang}\item_dlc2.msgbnd.dcx", read);
                }
            }
            else if (EldenRing)
            {
                ItemFMGs = read($@"{Dir}\Vanilla\msg\engus\item.msgbnd.dcx");
                ItemFMGs = MaybeOverrideFromModDir(ItemFMGs, @"msg\engus\item.msgbnd.dcx", read);
                MenuFMGs = read($@"{Dir}\Vanilla\msg\engus\menu.msgbnd.dcx");
                MenuFMGs = MaybeOverrideFromModDir(MenuFMGs, @"msg\engus\menu.msgbnd.dcx", read);
                foreach (string lang in MiscSetup.Langs.Keys)
                {
                    if (lang == "engus") continue;
                    OtherMenuFMGs[lang] = read($@"{Dir}\Vanilla\msg\{lang}\menu.msgbnd.dcx");
                    OtherMenuFMGs[lang] = MaybeOverrideFromModDir(OtherMenuFMGs[lang], $@"msg\{lang}\menu.msgbnd.dcx", read);
                    OtherItemFMGs[lang] = read($@"{Dir}\Vanilla\msg\{lang}\item.msgbnd.dcx");
                    OtherItemFMGs[lang] = MaybeOverrideFromModDir(OtherItemFMGs[lang], $@"msg\{lang}\item.msgbnd.dcx", read);
                }
            }
        }

        // TODO: Instead of doing this, make the paths themselves more editable?
        private T MaybeOverrideFromModDir<T>(T original, string path, Func<string, T> parser)
        {
            if (ModDir == null) return original;
            string modPath = $@"{ModDir}\{path}";
            if (File.Exists(modPath))
            {
                Console.WriteLine($"Using modded file {modPath}");
                return parser(modPath);
            }
            return original;
        }

        private void MaybeOverrideFromModDir<T>(Dictionary<string, T> files, Func<string, string> relpath, Func<string, T> parser)
        {
            if (ModDir == null) return;
            foreach (string key in files.Keys.ToList())
            {
                files[key] = MaybeOverrideFromModDir(files[key], relpath(key), parser);
            }
        }

        // Some helper functionality things. These require strict params
        public void SearchParamInt(uint id, string field=null)
        {
            bool matches(string cell)
            {
                // if (cell == id.ToString()) return true;
                if (cell.Contains(id.ToString())) return true;
                // if (int.TryParse(cell, out int val)) return val >= 11000000 && val <= 13000000 && (val / 1000) % 10 == 5;
                return false;
            }
            Console.WriteLine($"-- Searching params for {id}");
            foreach (KeyValuePair<string, PARAM> param in Params.Inner)
            {
                foreach (PARAM.Row row in param.Value.Rows)
                {
                    if (field == null && row.ID == id)
                    {
                        Console.WriteLine($"{param.Key}[{row.ID}]");
                    }
                    foreach (PARAM.Cell cell in row.Cells)
                    {
                        if ((field == null || cell.Def.InternalName == field) && cell.Value != null && matches(cell.Value.ToString()))
                        {
                            Console.WriteLine($"{param.Key}[{row.ID}].{cell.Def.InternalName} = {cell.Value}");
                        }
                    }
                }
            }
        }

        public void SearchParamFloat(float id)
        {
            Console.WriteLine($"-- Searching params for {id}");
            foreach (KeyValuePair<string, PARAM> param in Params.Inner)
            {
                foreach (PARAM.Row row in param.Value.Rows)
                {
                    foreach (PARAM.Cell cell in row.Cells)
                    {
                        if (cell.Value != null && cell.Value.GetType() == 0f.GetType() && Math.Abs((float)cell.Value - id) < 0.0001)
                        {
                            Console.WriteLine($"{param.Key}[{row.ID}].{cell.Def.InternalName} = {cell.Value}");
                        }
                    }
                }
            }
        }

        /// <param name="width">The number of consecuritve IDs to allocate.</param>
        /// <returns>
        /// An event flag ID that's guaranteed not to be used by any other events.
        /// </returns>
        /// <returns>Align the event so that it's a multiple of this number.</returns>
        public int GetUniqueEventId(int width = 1, int align = 1)
        {
            // Assume ranges are sorted from lowest to highest.
            var range = eventIdRanges.Find(range => nextEventId < range.End.Value);
            if (range.Equals(default))
            {
                throw new Exception("We've run out of known-safe event IDs!");
            }
            // Move the event into the next valid range.
            nextEventId = Math.Max(nextEventId, range.Start.Value);

            if (nextEventId % align != 0) nextEventId += align - (nextEventId % align);
            var value = nextEventId;
            nextEventId += width;

            return range.Contains(nextEventId - 1) ? value : GetUniqueEventId();
        }

        public void DumpMessages(string dir)
        {
            foreach (string path in Directory.GetFiles(dir, "*.msgbnd*"))
            {
                if (path.Contains("dlc1")) continue;
                string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                try
                {
                    IBinder bnd = BND3.Is(path) ? (IBinder)BND3.Read(path) : BND4.Read(path);
                    foreach (BinderFile file in bnd.Files)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name));
                        string uname = fileName;
                        // uname = System.Text.RegularExpressions.Regex.Replace(uname, @"[^\x00-\x7F]", c => string.Format(@"u{0:x4}", (int)c.Value[0]));
                        string fname = $"{name}_{uname}.txt";
                        // Console.WriteLine(fname);
                        // string fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name));
                        FMG fmg = FMG.Read(file.Bytes);
                        if (fmg.Entries != null)
                        {
                            File.WriteAllLines($@"{dir}\{fname}", fmg.Entries.Select(e => $"{e.ID} {(e.Text == null ? "" : e.Text.Replace("\r", "").Replace("\n", "\\n"))}"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load file: {name}: {path}\r\n\r\n{ex}");
                }
            }
            foreach (string path in Directory.GetFiles(dir, "*.fmg"))
            {
                FMG fmg = FMG.Read(path);
                string fname = Path.GetFileNameWithoutExtension(path);
                if (fmg.Entries != null)
                {
                    File.WriteAllLines($@"{dir}\{fname}.txt", fmg.Entries.Select(e => $"{e.ID} {(e.Text == null ? "" : e.Text.Replace("\r", "").Replace("\n", "\\n"))}"));
                }
            }
        }
    }
}
