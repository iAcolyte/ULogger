using System.Runtime.CompilerServices;

// SwapQueue, AsyncFileWriter and MainThreadDispatcher.PumpOnce are internal on purpose -- they are
// implementation, not API -- but they are also the parts most worth testing, so the test assemblies
// are let in rather than the types being widened to public.
[assembly: InternalsVisibleTo("ULogger.Tests.EditMode")]
[assembly: InternalsVisibleTo("ULogger.Tests.PlayMode")]
