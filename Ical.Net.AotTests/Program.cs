//
// Copyright ical.net project maintainers and contributors.
// Licensed under the MIT license.
//

using System;
using Ical.Net.AotTests;

// An AOT build that compiles clean and exits 0 proves nothing - that is exactly the state this
// library was in while it emitted a duplicated calendar event. This gate publishes a native
// binary, runs it, and diffs its transcript against the same fixtures under the JIT.
Console.WriteLine("# ical.net AOT smoke transcript");

Harness.Run("recurrence-id-override-suppression", Fixtures.RecurrenceIdOverrideSuppression);
Harness.Run("recurrence-rules", Fixtures.RecurrenceRules);
Harness.Run("exception-dates", Fixtures.ExceptionDates);
Harness.Run("recurrence-dates", Fixtures.RecurrenceDates);
Harness.Run("daylight-saving-boundary", Fixtures.DaylightSavingBoundary);
Harness.Run("vtimezone-round-trip", Fixtures.VTimeZoneRoundTrip);
Harness.Run("serialization-stability", Fixtures.SerializationStability);
Harness.Run("copy-every-type", Fixtures.CopyEveryType);
Harness.Run("copy-is-deep", Fixtures.CopyIsDeep);
Harness.Run("enum-parsing", Fixtures.EnumParsing);
Harness.Run("service-resolution", Fixtures.ServiceResolution);

return Harness.Complete();
