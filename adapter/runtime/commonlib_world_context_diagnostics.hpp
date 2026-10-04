#pragma once

namespace dovahlink::adapter::runtime {

///  Reads and logs a bounded diagnostic sample of Skyrim's world context.
///  Call only from the Skyrim game thread. The implementation is compiled out
///  of optimized builds and limits output to one sample per second.
void CaptureWorldContextDiagnostics();

} //  namespace dovahlink::adapter::runtime
