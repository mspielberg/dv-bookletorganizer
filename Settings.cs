using UnityModManagerNet;

namespace DvMod.BookletOrganizer
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Seperate rows per job type")]
        public bool groupRows = true;
        [Draw("Create booklets front to back")]
        public bool frontToBack = false;
        
        [Draw("Enable logging")]
        public bool enableLogging = false;

        public readonly string? version = Main.mod?.Info.Version;

        public override void Save(UnityModManager.ModEntry entry)
        {
            Save(this, entry);
        }

        public void OnChange()
        { }
    }
}
