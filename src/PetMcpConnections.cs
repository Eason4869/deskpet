namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        McpConnectionManager mcpConnections;
        public McpConnectionManager McpConnections {get{return mcpConnections??(mcpConnections=new McpConnectionManager());}}
    }
}
