using UnityModManagerNet;

namespace DvMod.BookletOrganizer
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Enable logging")] public bool enableLogging = false;
        [Draw("Seperate rows per job type")] public bool groupRows = true;
        [Draw("Create booklets front to back")] public bool frontToBack = false;
        public readonly string? version = Main.mod?.Info.Version;

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }

        public void OnChange()
        {
        }
    }
}
