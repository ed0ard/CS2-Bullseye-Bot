namespace BotAimImprover;

internal static class Signatures
{
    // CCSBot::PickNewAimSpot, CCSBot::GetPartPosition, CCSBot::IsVisible(Vector),
    // and the server routine used by its body-part cache to obtain posed hitboxes.
    internal static readonly string Pick = OperatingSystem.IsWindows()
        ? "48 8B C4 55 57 48 8D 68 ? 48 81 EC ? ? ? ? 48 8B F9 0F 29 70 ? 8B 89 ? ? ? ? 83 F9 FF"
        : "55 48 89 E5 41 55 41 54 53 48 89 FB 48 83 EC 58 8B 8F ? ? 00 00 83 F9 FF";
    internal static readonly string Part = OperatingSystem.IsWindows()
        ? "4C 8B DC 49 89 5B 08 49 89 6B 10 49 89 73 18 57 48 83 EC ? 49 C7 43 ? 32 02 00 00"
        : "55 48 8D 05 ? ? ? ? 48 8D 3D ? ? ? ? 48 89 E5 41 57 41 56 41 55 41 54 41 89 D4 53 48 8D 55 ? 48 89 F3 48 8D 35 ? ? ? ? 48 83 EC ? 48 89 45 ? 48 8D 05 ? ? ? ? C7 45 ? 32 02 00 00";
    // Same signature as Smarter-Bot so CSS can reuse an already hooked binding.
    internal static readonly string Visible = OperatingSystem.IsWindows()
        ? "48 89 5C 24 ? 48 89 74 24 ? 48 89 7C 24 ? 55 41 54 41 55 41 56 41 57 48 8D AC 24 ? ? ? ? 48 81 EC ? ? ? ? 48 8D 05 ? ? ? ? 48 C7 45 ? ? ? ? ? 48 89 45 ? ? ? ? ? 0F 10 45 ? 48 8D 05 ? ? ? ? 4C 8B E2"
        : "55 48 8D 05 ? ? ? ? 48 89 E5 41 57 4C 8D BD ? ? ? ? 41 56 41 55 41 54 49 89 CC 53 48 89 FB 48 8D 3D ? ? ? ? 48 81 EC ? ? ? ? 48 89 B5 ? ? ? ? 89 95 ? ? ? ? 4C 89 FA";
    internal static readonly string Boxes = OperatingSystem.IsWindows()
        ? "48 89 5C 24 20 57 41 56 41 57 48 83 EC 50 48 8B F9 41 8B D9 48 8B 89 ? ? ? ? 4D 8B F0 4C 8B FA 48 85 C9"
        : "55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC 38 48 8B 87 ? ? ? ? 48 85 C0 0F 84 ? ? ? ? 4C 8B 30 4D 85 F6";
}
