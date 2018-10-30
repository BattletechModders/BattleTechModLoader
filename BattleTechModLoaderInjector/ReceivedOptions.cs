using System;
namespace BattleTechModLoader
{
    /***
     * A container for the values passed by the user into the program via command line.
     * Interacts with OptionSet in the main injector class.
     */
    internal class ReceivedOptions
    {
        public bool requireKeyPress = true;
        public bool detecting = false;
        public string requiredGameVersion = String.Empty;
        public string requiredGameVersionMismatchMessage = String.Empty;
        public string managedDir = String.Empty;
        public bool gameVersion = false;
        public bool helping = false;
        public bool installing = true;
        public bool restoring = false;
        public bool updating = false;
        public bool versioning = false;
    }
}
