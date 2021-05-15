namespace DiscordBoardGame
{
    public class Player
    {
        public string Avatar { get; internal set; }
        public string Name { get; internal set; }
        public int Position { get; internal set; }
        public bool Rolled { get; set; }
    }
}