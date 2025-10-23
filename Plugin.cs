using System;
using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MySqlConnector;
using Exiled.API.Enums;

namespace PlaytimeTracker
{
    public class PlaytimeTracker : Plugin<Config>
    {
        public override string Name => "PlaytimeTracker";
        public override string Author => "Naleśnior";
        public override Version Version => new Version(5, 0, 0);
        public override Version RequiredExiledVersion => new Version(9, 6, 1);

        private string connectionString;
        private Dictionary<string, DateTime> PlayerJoinTimes = new Dictionary<string, DateTime>();

        public override void OnEnabled()
        {
            base.OnEnabled();
            connectionString = Config.ConnectionString;
            CreateDatabase();
            Exiled.Events.Handlers.Player.Verified += OnPlayerVerified;
            Exiled.Events.Handlers.Player.Left += OnPlayerLeft;
            Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        }

        public override void OnDisabled()
        {
            Exiled.Events.Handlers.Player.Verified -= OnPlayerVerified;
            Exiled.Events.Handlers.Player.Left -= OnPlayerLeft;
            Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
            base.OnDisabled();
        }

        private void OnPlayerVerified(VerifiedEventArgs ev)
        {
            if (string.IsNullOrEmpty(ev.Player.UserId))
            {
                Log.Warn($"Gracz {ev.Player.Nickname} ma pusty UserId! Ignorowanie...");
                return;
            }

            Log.Info($"Gracz {ev.Player.Nickname} dołączył z UserId: {ev.Player.UserId}");
            PlayerJoinTimes[ev.Player.UserId] = DateTime.UtcNow;

            string adminRole = ev.Player.GroupName ?? "Gracz";

            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    string updateNicknameQuery = @"
                INSERT INTO PlayerStats (PlayerId, Nickname, TotalTime, AdminRole) 
                VALUES (@PlayerId, @Nickname, 0, @AdminRole)
                ON DUPLICATE KEY UPDATE Nickname = @Nickname, AdminRole = @AdminRole;";

                    using (var command = new MySqlCommand(updateNicknameQuery, connection))
                    {
                        command.Parameters.AddWithValue("@PlayerId", ev.Player.UserId);
                        command.Parameters.AddWithValue("@Nickname", ev.Player.Nickname);
                        command.Parameters.AddWithValue("@AdminRole", adminRole);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Błąd podczas aktualizacji nicku/roli gracza: {ex.Message}");
            }
        }

        private void OnPlayerLeft(LeftEventArgs ev)
        {
            if (PlayerJoinTimes.TryGetValue(ev.Player.UserId, out DateTime joinTime))
            {
                DateTime leaveTime = DateTime.UtcNow;
                double sessionSeconds = (leaveTime - joinTime).TotalSeconds;

                Log.Info($"Gracz {ev.Player.Nickname} ({ev.Player.UserId}) opuścił serwer. Czas sesji: {sessionSeconds} sekund.");

                try
                {
                    UpdatePlayerTime(ev.Player.UserId, joinTime, leaveTime, sessionSeconds);
                }
                catch (Exception ex)
                {
                    Log.Error($"Błąd podczas aktualizacji czasu gracza w bazie danych: {ex.Message}");
                }

                PlayerJoinTimes.Remove(ev.Player.UserId);
            }
        }

        private void OnChangingRole(ChangingRoleEventArgs ev)
        {
            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    string insertRoleQuery = @"
                        INSERT INTO PlayerRoles (PlayerId, RoleName, TimeChanged)
                        VALUES (@PlayerId, @RoleName, @TimeChanged);";

                    using (var command = new MySqlCommand(insertRoleQuery, connection))
                    {
                        command.Parameters.AddWithValue("@PlayerId", ev.Player.UserId);
                        command.Parameters.AddWithValue("@RoleName", ev.NewRole.ToString());
                        command.Parameters.AddWithValue("@TimeChanged", DateTime.UtcNow);
                        command.ExecuteNonQuery();
                    }
                }
                Log.Info($"Zapisano zmianę roli gracza {ev.Player.Nickname} na {ev.NewRole}.");
            }
            catch (Exception ex)
            {
                Log.Error($"Błąd podczas zapisywania roli gracza: {ex.Message}");
            }
        }

        private void CreateDatabase()
        {
            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    string createPlayerStats = @"
                        CREATE TABLE IF NOT EXISTS PlayerStats (
                            PlayerId VARCHAR(255) PRIMARY KEY,
                            TotalTime DOUBLE NOT NULL,
                            Nickname VARCHAR(255) NOT NULL,
                            AdminRole VARCHAR(64) DEFAULT NULL
                        );";

                    string createPlayerSessions = @"
                        CREATE TABLE IF NOT EXISTS PlayerSessions (
                            SessionId INT AUTO_INCREMENT PRIMARY KEY,
                            PlayerId VARCHAR(255),
                            SessionStart DATETIME,
                            SessionEnd DATETIME,
                            SessionTime DOUBLE,
                            FOREIGN KEY (PlayerId) REFERENCES PlayerStats(PlayerId)
                        );";

                    string createPlayerRoles = @"
                        CREATE TABLE IF NOT EXISTS PlayerRoles (
                            RoleId INT AUTO_INCREMENT PRIMARY KEY,
                            PlayerId VARCHAR(255),
                            RoleName VARCHAR(255),
                            TimeChanged DATETIME,
                            FOREIGN KEY (PlayerId) REFERENCES PlayerStats(PlayerId)
                        );";

                    Log.Info("Tworzenie tabeli PlayerStats...");
                    using (var command = new MySqlCommand(createPlayerStats, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    Log.Info("Tabela PlayerStats utworzona.");

                    Log.Info("Tworzenie tabeli PlayerSessions...");
                    using (var command = new MySqlCommand(createPlayerSessions, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    Log.Info("Tabela PlayerSessions utworzona.");

                    Log.Info("Tworzenie tabeli PlayerRoles...");
                    using (var command = new MySqlCommand(createPlayerRoles, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    Log.Info("Tabela PlayerRoles utworzona.");
                }
                Log.Info("Tabele zostały poprawnie utworzone.");
            }
            catch (Exception ex)
            {
                Log.Error($"Błąd podczas tworzenia tabel w MySQL: {ex.Message}");
            }
        }

        private MySqlConnection GetDatabaseConnection()
        {
            try
            {
                Log.Info($"Otwieram bazę danych");
                var connection = new MySqlConnection(connectionString);
                connection.Open();
                Log.Info("Połączenie z bazą danych nawiązane.");
                return connection;
            }
            catch (Exception ex)
            {
                Log.Error($"Błąd podczas otwierania bazy danych: {ex.Message}");
                throw;
            }
        }

        private void UpdatePlayerTime(string playerId, DateTime joinTime, DateTime leaveTime, double sessionSeconds)
        {
            try
            {
                using (var connection = GetDatabaseConnection())
                {
                    string selectQuery = "SELECT COUNT(*) FROM PlayerStats WHERE PlayerId = @PlayerId";
                    using (var selectCmd = new MySqlCommand(selectQuery, connection))
                    {
                        selectCmd.Parameters.AddWithValue("@PlayerId", playerId);
                        int count = Convert.ToInt32(selectCmd.ExecuteScalar());
                        Log.Info($"Liczba rekordów dla gracza {playerId}: {count}");

                        if (count == 0)
                        {
                            string insertQuery = "INSERT INTO PlayerStats (PlayerId, TotalTime) VALUES (@PlayerId, @Time)";
                            using (var insertCmd = new MySqlCommand(insertQuery, connection))
                            {
                                insertCmd.Parameters.AddWithValue("@PlayerId", playerId);
                                insertCmd.Parameters.AddWithValue("@Time", sessionSeconds);
                                int rowsAffected = insertCmd.ExecuteNonQuery();
                                Log.Info($"Wstawiono {rowsAffected} rekordów do tabeli PlayerStats dla gracza {playerId}");
                            }
                        }
                        else
                        {
                            string updateQuery = "UPDATE PlayerStats SET TotalTime = TotalTime + @Time WHERE PlayerId = @PlayerId";
                            using (var updateCmd = new MySqlCommand(updateQuery, connection))
                            {
                                updateCmd.Parameters.AddWithValue("@Time", sessionSeconds);
                                updateCmd.Parameters.AddWithValue("@PlayerId", playerId);
                                int rowsAffected = updateCmd.ExecuteNonQuery();
                                Log.Info($"Zaktualizowano {rowsAffected} rekordów w tabeli PlayerStats dla gracza {playerId}");
                            }
                        }
                    }

                    string insertSession = "INSERT INTO PlayerSessions (PlayerId, SessionStart, SessionEnd, SessionTime) VALUES (@PlayerId, @Start, @End, @Time)";
                    using (var sessionCmd = new MySqlCommand(insertSession, connection))
                    {
                        sessionCmd.Parameters.AddWithValue("@PlayerId", playerId);
                        sessionCmd.Parameters.AddWithValue("@Start", joinTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        sessionCmd.Parameters.AddWithValue("@End", leaveTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        sessionCmd.Parameters.AddWithValue("@Time", sessionSeconds);
                        int rowsAffected = sessionCmd.ExecuteNonQuery();
                        Log.Info($"Wstawiono {rowsAffected} rekordów do tabeli PlayerSessions dla gracza {playerId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Błąd podczas aktualizacji danych gracza: {ex.Message}");
            }
        }
    }
}