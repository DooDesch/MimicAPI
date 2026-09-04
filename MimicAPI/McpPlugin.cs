using System;
using System.Collections.Generic;
using Mimic.Actors;
using MimicAPI.GameAPI;
using UnityEngine;

namespace MimicAPI
{
    /// <summary>
    /// Macht das Wissen dieser Bibliothek fuer ein MCP-Werkzeug erreichbar, ohne dass eine der
    /// beiden Seiten die andere kennt.
    ///
    /// Eine Bruecke sucht zur Laufzeit nach einem Typ dieses Namens und liest seine statischen
    /// Felder. Alle Signaturen benutzen ausschliesslich Typen der Standardbibliothek - Func,
    /// Dictionary, string, object - also braucht MimicAPI keine Referenz auf die Bruecke und die
    /// Bruecke keine auf MimicAPI. Ist keine Bruecke geladen, liegt diese Klasse ungenutzt herum
    /// und kostet nichts.
    ///
    /// Deshalb wird sie auch unbedingt ausgeliefert und nicht hinter #if DEBUG versteckt: sie hat
    /// keine Abhaengigkeit, die ein Spieler nicht haette, und ein Werkzeug soll nicht deshalb
    /// blind sein, weil jemand einen Release-Build installiert hat.
    ///
    /// Jeder Befehl laeuft auf dem Unity-Hauptthread, darf also direkt an Spielobjekte. Und jeder
    /// gibt nur zurueck, was er wirklich weiss: eine Reflexionsschicht kann nach einem
    /// Spiel-Update ins Leere greifen, und dann ist "unbekannt" die richtige Antwort und nicht 0.
    /// </summary>
    public static class McpPlugin
    {
        /// <summary>Muss zur Bruecke passen. Bei Abweichung wird nicht gebunden statt falsch gebunden.</summary>
        public const int AbiVersion = 1;

        public static string GameId = "mimesis";

        public static Dictionary<string, Func<Dictionary<string, object>, Dictionary<string, object>>> Commands =
            new Dictionary<string, Func<Dictionary<string, object>, Dictionary<string, object>>>(StringComparer.Ordinal)
            {
                { "get_session_state", GetSessionState },
                { "get_world_state", GetWorldState },
                { "list_rooms", ListRooms },
                { "get_room", GetRoom },
                { "get_players", GetPlayers },
            };

        /// <summary>
        /// Fliesst in die Lebendpruefung der Bruecke ein, wird also haeufig und nebenbei abgefragt.
        /// Deshalb nur zwei billige Werte und kein Durchlaufen aller Raeume.
        /// </summary>
        public static Func<Dictionary<string, object>> Health = delegate ()
        {
            Dictionary<string, object> map = New();
            try
            {
                map["mimicApi"] = true;
                map["hasWorld"] = CoreAPI.GetVWorld() != null;
                map["hasLocalPlayer"] = PlayerAPI.HasLocalPlayer();
            }
            catch (Exception ex)
            {
                map["mimicApiError"] = Explain(ex);
            }
            return map;
        };

        /// <summary>
        /// Sagt der Bruecke, ob wir Gastgeber sind. Davon haengt ab, ob sie aendernde Befehle
        /// zulaesst: MIMESIS ist gastgeberautoritativ, eine Aenderung auf der Clientseite ist
        /// entweder wirkungslos oder ein Desync.
        ///
        /// VWorld existiert nur beim Gastgeber - das ist hier das belastbare Merkmal, nicht eine
        /// Spielerzahl.
        /// </summary>
        public static Func<Dictionary<string, object>> Session = delegate ()
        {
            Dictionary<string, object> map = New();
            try
            {
                bool isHost = CoreAPI.GetVWorld() != null;
                map["isHost"] = isHost;
                map["peerCount"] = (double)Math.Max(0, ServerNetworkAPI.GetSessionCount() - 1);
            }
            catch (Exception ex)
            {
                // Im Zweifel Gastgeber melden. Ein falsches "Mitspieler" wuerde jeden aendernden
                // Befehl sperren, und eine Bruecke, die aus Unsicherheit blockiert, ist unbrauchbar.
                map["isHost"] = true;
                map["peerCount"] = (double)0;
                map["sessionError"] = Explain(ex);
            }
            return map;
        };

        public static Func<Dictionary<string, object>> Describe = delegate ()
        {
            Dictionary<string, object> map = New();
            map["get_session_state"] = Doc("Gastgeberrolle, Sitzungszahl, maximale Spielerzahl.", null);
            map["get_world_state"] = Doc("VWorld, Raumverwaltung und der Raum, in dem der lokale Spieler steht.", null);
            map["list_rooms"] = Doc("Alle Raeume mit Kennung, Typ, Mitgliederzahl und Zyklus.", null);
            map["get_room"] = Doc("Ein Raum im Detail.", "roomId (Zahl), sonst der aktuelle Raum");
            map["get_players"] = Doc("Alle Spieler mit Name, Position und Lebendstatus.", null);
            return map;
        };

        // ------------------------------------------------------------------ Befehle

        private static Dictionary<string, object> GetSessionState(Dictionary<string, object> args)
        {
            Dictionary<string, object> map = New();
            map["isHost"] = Try(delegate { return (object)(CoreAPI.GetVWorld() != null); }, map, "isHost");
            map["sessionCount"] = Try(delegate { return (object)(double)ServerNetworkAPI.GetSessionCount(); }, map, "sessionCount");
            map["maximumClients"] = Try(delegate { return (object)(double)ServerNetworkAPI.GetMaximumClients(); }, map, "maximumClients");
            map["serverRunning"] = Try(delegate { return (object)ServerNetworkAPI.IsServerRunning(); }, map, "serverRunning");
            return map;
        }

        private static Dictionary<string, object> GetWorldState(Dictionary<string, object> args)
        {
            Dictionary<string, object> map = New();
            map["hasWorld"] = Try(delegate { return (object)(CoreAPI.GetVWorld() != null); }, map, "hasWorld");
            map["hasRoomManager"] = Try(delegate { return (object)(CoreAPI.GetVRoomManager() != null); }, map, "hasRoomManager");
            map["hasLocalPlayer"] = Try(delegate { return (object)PlayerAPI.HasLocalPlayer(); }, map, "hasLocalPlayer");

            object current = Try(delegate { return RoomAPI.GetCurrentRoom(); }, map, "currentRoom");
            map["currentRoom"] = current == null ? null : (object)DescribeRoom(current, false);
            return map;
        }

        private static Dictionary<string, object> ListRooms(Dictionary<string, object> args)
        {
            Dictionary<string, object> map = New();
            List<object> rooms = new List<object>();
            try
            {
                foreach (object room in RoomAPI.GetAllRooms())
                {
                    if (room == null) continue;
                    rooms.Add(DescribeRoom(room, false));
                }
            }
            catch (Exception ex)
            {
                map["error"] = Explain(ex);
            }
            map["count"] = (double)rooms.Count;
            map["rooms"] = rooms;
            return map;
        }

        private static Dictionary<string, object> GetRoom(Dictionary<string, object> args)
        {
            Dictionary<string, object> map = New();
            object room = null;
            try
            {
                long roomId = ReadLong(args, "roomId", 0L);
                room = roomId != 0L ? RoomAPI.GetRoom(roomId) : RoomAPI.GetCurrentRoom();
            }
            catch (Exception ex)
            {
                map["error"] = Explain(ex);
                return map;
            }

            if (room == null)
            {
                map["found"] = false;
                map["note"] = "Kein Raum. Ohne roomId wird der Raum des lokalen Spielers genommen, und im Menue gibt es keinen.";
                return map;
            }

            map["found"] = true;
            map["room"] = DescribeRoom(room, true);
            return map;
        }

        private static Dictionary<string, object> GetPlayers(Dictionary<string, object> args)
        {
            Dictionary<string, object> map = New();
            List<object> players = new List<object>();
            try
            {
                ProtoActor[] actors = PlayerAPI.GetAllPlayers();
                if (actors != null)
                {
                    foreach (ProtoActor actor in actors)
                    {
                        if (actor == null) continue;
                        Dictionary<string, object> entry = New();
                        entry["name"] = PlayerAPI.GetPlayerName(actor);
                        entry["alive"] = PlayerAPI.IsPlayerAlive(actor);
                        Vector3 position = PlayerAPI.GetPlayerPosition(actor);
                        entry["x"] = (double)position.x;
                        entry["y"] = (double)position.y;
                        entry["z"] = (double)position.z;
                        players.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                map["error"] = Explain(ex);
            }
            map["count"] = (double)players.Count;
            map["players"] = players;
            map["hasLocalPlayer"] = Try(delegate { return (object)PlayerAPI.HasLocalPlayer(); }, map, "hasLocalPlayer");
            return map;
        }

        // ------------------------------------------------------------------ Hilfsmittel

        private static Dictionary<string, object> DescribeRoom(object room, bool detailed)
        {
            Dictionary<string, object> map = New();
            map["id"] = Try(delegate { return (object)(double)RoomAPI.GetRoomID(room); }, map, "id");
            map["type"] = Try(delegate { return (object)RoomAPI.GetRoomName(room); }, map, "type");
            map["playable"] = Try(delegate { return (object)RoomAPI.IsRoomPlayable(room); }, map, "playable");
            map["members"] = Try(delegate { return (object)(double)RoomAPI.GetMemberCount(room); }, map, "members");
            map["cycle"] = Try(delegate { return (object)(double)RoomAPI.GetCurrentGameDay(room); }, map, "cycle");

            if (!detailed) return map;

            map["masterId"] = Try(delegate { return (object)(double)RoomAPI.GetRoomMasterID(room); }, map, "masterId");
            map["currency"] = Try(delegate { return (object)(double)RoomAPI.GetRoomCurrency(room); }, map, "currency");
            map["sessionCycle"] = Try(delegate { return (object)(double)RoomAPI.GetCurrentSessionCycle(room); }, map, "sessionCycle");
            map["tick"] = Try(delegate { return (object)(double)RoomAPI.GetCurrentTick(room); }, map, "tick");
            map["deadPlayers"] = Try(delegate { return (object)(double)RoomAPI.GetDeadPlayerCount(room); }, map, "deadPlayers");
            map["allPlayersDead"] = Try(delegate { return (object)RoomAPI.IsAllPlayerDead(room); }, map, "allPlayersDead");
            map["playerCount"] = Try(delegate { return (object)(double)RoomAPI.GetRoomPlayerCount(room); }, map, "playerCount");
            return map;
        }

        /// <summary>
        /// Fuehrt eine Abfrage aus und traegt einen Fehlschlag namentlich ein, statt ihn zu
        /// verschlucken. Hinter jedem dieser Aufrufe steht Reflection, die nach einem Spiel-Update
        /// ins Leere greifen kann - dann ist "diese eine Angabe fehlt, und zwar deshalb" die
        /// brauchbare Antwort, waehrend eine 0 wie ein Messwert aussaehe.
        /// </summary>
        private static object Try(Func<object> read, Dictionary<string, object> map, string name)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                map[name + "Error"] = Explain(ex);
                return null;
            }
        }

        private static Dictionary<string, object> Doc(string description, string arguments)
        {
            Dictionary<string, object> map = New();
            map["description"] = description;
            map["args"] = arguments ?? "";
            return map;
        }

        private static Dictionary<string, object> New()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private static long ReadLong(Dictionary<string, object> args, string key, long fallback)
        {
            object raw;
            if (args == null || !args.TryGetValue(key, out raw) || raw == null) return fallback;
            if (raw is double) return (long)(double)raw;
            if (raw is long) return (long)raw;
            if (raw is int) return (int)raw;
            long parsed;
            string text = raw as string;
            if (text != null && long.TryParse(text, out parsed)) return parsed;
            return fallback;
        }

        private static string Explain(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
