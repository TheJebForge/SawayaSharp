using Lavalink4NET.Tracks;

namespace SawayaSharp.Data;

public class PlaylistInfo
{
    public string Id { get; set; } = "";
    
    public string Name { get; set; } = "";
    
    public ulong Owner { get; set; }

    public List<ulong> Contributors { get; set; } = new();
    
    public List<LavalinkTrack> Tracks { get; set; } = new();
}