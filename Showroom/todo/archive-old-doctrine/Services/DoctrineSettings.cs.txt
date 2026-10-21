namespace ShowroomDoctrine;

/// <summary>How the replay plays on this device. A service so a proof can switch the autoplay off and read the pace; the app uses the defaults.</summary>
public interface IDoctrineSettings
{
    /// <summary>The replay starts playing by itself when a round's battle has been simulated.</summary>
    bool AutoPlay { get; }
    /// <summary>Milliseconds one tick of the recording takes at 1x.</summary>
    int TickMs { get; }
}

public sealed class DoctrineSettings : IDoctrineSettings
{
    public bool AutoPlay { get; init; } = true;
    public int TickMs { get; init; } = 140;
}

/// <summary>Optional: a proof registers one to give the page its own sections (for example a general that fills the per-slot match record). The app registers none.</summary>
public sealed class DoctrineSectionsSource(Func<DoctrineSections> make)
{
    public Func<DoctrineSections> Make { get; } = make;
}
