namespace TheAdventure
{
public class Layer
{
    public required int[] Data { get; set; }
    public int Height { get; set; }
    public int Width {get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Visible { get; set; }
    public int Id { get; set; }
}
}