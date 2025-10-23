using Exiled.API.Interfaces;

namespace PlaytimeTracker
{
    public class Config : IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string ConnectionString { get; set; } = "Server=IP_OF_YOUR_DATABASE;Port=YOUR_DATABASE_PORT;Database=DATABASE_NAME;Uid=YOUR_DATABASE_USER;Pwd=NAME_OF_YOUR_DATABASE_PASSWORD;";
    }
}
