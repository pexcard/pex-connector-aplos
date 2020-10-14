namespace Aplos.Api.Client.Models.Response
{
    public abstract class AplosApiDataResponse<TData> : AplosApiResponse
    {
        public TData Data { get; set; }
        public AplosApiLinks Links { get; set; }
    }
}