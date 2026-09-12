using NektoTranslate.System.Contracts;


namespace NektoTranslate.System.Services;


public interface IReleaseChecker {

    Task<UpdateAvailability> CheckAsync(CancellationToken cancellationToken = default);
}
