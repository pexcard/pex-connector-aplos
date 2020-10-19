namespace Aplos.Api.Client.Models.Response
{
    public abstract class AplosApiResponse
    {
        public string Version { get; set; }
        public int Status { get; set; }
    }
}