using System.Threading.Tasks;

namespace Wasil.Service.Interfaces;

public interface ISmsService
{
    Task SendOtpSmsAsync(string phone, string code);
}
