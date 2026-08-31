//
// Copyright ical.net project maintainers and contributors.
// Licensed under the MIT license.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Ical.Net.Serialization.DataTypes;
using NodaTime;

namespace Ical.Net.AotTests;

internal static class Fixtures
{
    private const string Tz = "America/New_York";

    private static DateTimeZone Zone => CalendarTimeZoneProviders.TzdbWithAliases[Tz];

    private static string Ics(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Wrap(params string[] body) => Ics(
        new[] { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//ical.net//aot//EN" }
            .Concat(body)
            .Concat(new[] { "END:VCALENDAR" })
            .ToArray());

    /// <summary>Local wall time plus zone - the representation a divergence would show up in.</summary>
    private static string Fmt(ZonedDateTime value)
        => value.LocalDateTime.ToString("uuuu-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + value.Zone.Id;

    private static string Fmt(CalDateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "<null>";

    private static Instant At(int year, int month, int day)
        => Instant.FromUtc(year, month, day, 0, 0);

    /// <summary>Calendar.Load returns a nullable; a null here is a fixture bug, not a result.</summary>
    private static Calendar Load(string ics)
        => Calendar.Load(ics) ?? throw new InvalidOperationException("Calendar.Load returned null.");

    private static List<Occurrence> Occurrences(Calendar calendar, Instant from, Instant to)
        => calendar.GetOccurrences(Zone, from).TakeWhileBefore(to).ToList();

    /// <summary>
    /// The failure that motivated this whole gate: under TrimMode=full the reflective
    /// [OnDeserializing] scan never fired, the random GUID seeded by the UniqueComponent
    /// constructor survived as the first UID value, and RECURRENCE-ID matching - which is keyed on
    /// (Uid, Instant) - stopped suppressing the overridden occurrence.
    /// <para/>
    /// Asserting the occurrence count alone catches this only by luck: the corruption produced
    /// correct times with corrupted identities. Assert the parsed UID verbatim.
    /// </summary>
    internal static void RecurrenceIdOverrideSuppression()
    {
        var calendar = Load(Wrap(
            "BEGIN:VEVENT",
            "UID:evt1",
            "DTSTART;TZID=" + Tz + ":20260316T090000",
            "DTEND;TZID=" + Tz + ":20260316T100000",
            "RRULE:FREQ=DAILY;COUNT=3",
            "SUMMARY:Recurring",
            "END:VEVENT",
            "BEGIN:VEVENT",
            "UID:evt1",
            "RECURRENCE-ID;TZID=" + Tz + ":20260317T090000",
            "DTSTART;TZID=" + Tz + ":20260317T140000",
            "DTEND;TZID=" + Tz + ":20260317T150000",
            "SUMMARY:Moved",
            "END:VEVENT"));

        Harness.Check("event.count", calendar.Events.Count, 2);

        // The whole point: the parsed UID must survive verbatim, not be replaced by a random GUID.
        var uids = calendar.Events.Select(e => e.Uid).OrderBy(u => u, StringComparer.Ordinal).ToList();
        Harness.Check("event.uids", uids, new[] { "evt1", "evt1" });

        var ordered = calendar.Events.OrderBy(e => e.RecurrenceIdentifier is null ? 0 : 1).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            Harness.Report($"event[{i}].uid", ordered[i].Uid);
            Harness.Report($"event[{i}].summary", ordered[i].Summary);
            Harness.Report($"event[{i}].recurrenceId", Fmt(ordered[i].RecurrenceIdentifier?.StartTime));
        }

        var occurrences = Occurrences(calendar, At(2026, 3, 1), At(2026, 4, 1))
            .OrderBy(o => o.Start.ToInstant())
            .ToList();

        // 3 from the RRULE, with the 17th replaced - not appended - by the override.
        Harness.Check("occurrence.count", occurrences.Count, 3);
        for (var i = 0; i < occurrences.Count; i++)
        {
            Harness.Report($"occurrence[{i}].start", Fmt(occurrences[i].Start));
            Harness.Report($"occurrence[{i}].summary", (occurrences[i].Source as CalendarEvent)?.Summary);
        }
    }

    internal static void RecurrenceRules()
    {
        var calendar = Load(Wrap(
            "BEGIN:VEVENT",
            "UID:rrule-1",
            "DTSTART;TZID=" + Tz + ":20260105T090000",
            "DTEND;TZID=" + Tz + ":20260105T100000",
            "RRULE:FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=6",
            "SUMMARY:Last weekday of the month",
            "END:VEVENT"));

        var occurrences = Occurrences(calendar, At(2026, 1, 1), At(2027, 1, 1));

        Harness.Check("bysetpos.count", occurrences.Count, 6);
        for (var i = 0; i < occurrences.Count; i++)
        {
            Harness.Report($"bysetpos[{i}]", Fmt(occurrences[i].Start));
        }
    }

    internal static void ExceptionDates()
    {
        var calendar = Load(Wrap(
            "BEGIN:VEVENT",
            "UID:exdate-1",
            "DTSTART;TZID=" + Tz + ":20260105T090000",
            "DTEND;TZID=" + Tz + ":20260105T100000",
            "RRULE:FREQ=DAILY;COUNT=5",
            "EXDATE;TZID=" + Tz + ":20260106T090000,20260108T090000",
            "SUMMARY:With exceptions",
            "END:VEVENT"));

        var occurrences = Occurrences(calendar, At(2026, 1, 1), At(2026, 2, 1));

        Harness.Check("exdate.count", occurrences.Count, 3);
        for (var i = 0; i < occurrences.Count; i++)
        {
            Harness.Report($"exdate[{i}]", Fmt(occurrences[i].Start));
        }
    }

    /// <summary>
    /// RDATE produces a PeriodList, one of the internal types that cannot be constructed from
    /// here directly.
    /// </summary>
    internal static void RecurrenceDates()
    {
        var calendar = Load(Wrap(
            "BEGIN:VEVENT",
            "UID:rdate-1",
            "DTSTART;TZID=" + Tz + ":20260105T090000",
            "DTEND;TZID=" + Tz + ":20260105T100000",
            "RDATE;TZID=" + Tz + ":20260112T090000,20260119T090000",
            "SUMMARY:With RDATE",
            "END:VEVENT"));

        var occurrences = Occurrences(calendar, At(2026, 1, 1), At(2026, 2, 1));

        Harness.Check("rdate.count", occurrences.Count, 3);
        for (var i = 0; i < occurrences.Count; i++)
        {
            Harness.Report($"rdate[{i}]", Fmt(occurrences[i].Start));
        }

        var serializer = new CalendarSerializer();
        var once = serializer.SerializeToString(calendar)!;
        Harness.Check("rdate.stable", serializer.SerializeToString(Load(once)) == once, true);
    }

    /// <summary>
    /// Spring-forward in America/New_York happens on 2026-03-08. A daily event crossing the
    /// boundary keeps its local wall time while its UTC offset shifts.
    /// </summary>
    internal static void DaylightSavingBoundary()
    {
        var calendar = Load(Wrap(
            "BEGIN:VEVENT",
            "UID:dst-1",
            "DTSTART;TZID=" + Tz + ":20260306T093000",
            "DTEND;TZID=" + Tz + ":20260306T103000",
            "RRULE:FREQ=DAILY;COUNT=4",
            "SUMMARY:Across the DST boundary",
            "END:VEVENT"));

        var occurrences = Occurrences(calendar, At(2026, 3, 1), At(2026, 4, 1));

        Harness.Check("dst.count", occurrences.Count, 4);
        for (var i = 0; i < occurrences.Count; i++)
        {
            Harness.Report($"dst[{i}].local", Fmt(occurrences[i].Start));
            Harness.Report($"dst[{i}].utc", occurrences[i].Start.ToInstant().ToString());
        }
    }

    /// <summary>
    /// Covers VTimeZone and its private nested IntervalRecurrenceRule, one of the 28 types that
    /// needed a CreateNew() override.
    /// </summary>
    internal static void VTimeZoneRoundTrip()
    {
        var calendar = new Calendar();
        calendar.AddTimeZone(new VTimeZone(Tz));
        calendar.Events.Add(new CalendarEvent
        {
            Uid = "tz-1",
            DtStart = new CalDateTime(2026, 6, 1, 9, 0, 0, Tz),
            DtEnd = new CalDateTime(2026, 6, 1, 10, 0, 0, Tz),
            Summary = "Time zone round trip",
        });

        var serializer = new CalendarSerializer();
        var once = serializer.SerializeToString(calendar)!;
        var reloaded = Load(once);
        var twice = serializer.SerializeToString(reloaded)!;

        Harness.Check("vtimezone.stable", twice == once, true);
        Harness.Check("vtimezone.count", reloaded.TimeZones.Count, 1);
        Harness.Check("vtimezone.id", reloaded.TimeZones.FirstOrDefault()?.TzId, Tz);
        Harness.Report("vtimezone.length", once.Length);
    }

    /// <summary>
    /// serialize -> parse -> serialize over a document that exercises most of the property
    /// pipeline. Any instantiation the trimmer removed shows up as a missing or mistyped property.
    /// </summary>
    internal static void SerializationStability()
    {
        var source = Wrap(
            "BEGIN:VEVENT",
            "UID:round-trip-1",
            "DTSTAMP:20260101T000000Z",
            "DTSTART;TZID=" + Tz + ":20260105T090000",
            "DTEND;TZID=" + Tz + ":20260105T100000",
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4",
            "SUMMARY:Everything",
            "DESCRIPTION:A description with some text",
            "CATEGORIES:one,two,three",
            "PRIORITY:5",
            "SEQUENCE:2",
            "STATUS:CONFIRMED",
            "TRANSP:OPAQUE",
            "CLASS:PUBLIC",
            "LOCATION:Somewhere",
            "GEO:48.210033;16.363449",
            "URL:https://example.com/",
            "ORGANIZER;CN=The Organizer:mailto:organizer@example.com",
            "ATTENDEE;CN=An Attendee;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:attendee@example.com",
            "ATTACH;FMTTYPE=text/plain:https://example.com/file.txt",
            "REQUEST-STATUS:2.0;Success",
            "BEGIN:VALARM",
            "ACTION:DISPLAY",
            "DESCRIPTION:Reminder",
            "TRIGGER:-PT15M",
            "END:VALARM",
            "END:VEVENT",
            "BEGIN:VTODO",
            "UID:todo-1",
            "DTSTAMP:20260101T000000Z",
            "SUMMARY:A task",
            "DUE;TZID=" + Tz + ":20260110T170000",
            "PERCENT-COMPLETE:40",
            "END:VTODO",
            "BEGIN:VJOURNAL",
            "UID:journal-1",
            "DTSTAMP:20260101T000000Z",
            "SUMMARY:A journal entry",
            "END:VJOURNAL");

        var serializer = new CalendarSerializer();
        var once = serializer.SerializeToString(Load(source))!;
        var twice = serializer.SerializeToString(Load(once))!;

        Harness.Check("roundtrip.stable", twice == once, true);
        Harness.Report("roundtrip.length", once.Length);

        var calendar = Load(once);
        var evt = calendar.Events.First();

        Harness.Check("roundtrip.uid", evt.Uid, "round-trip-1");
        Harness.Check("roundtrip.summary", evt.Summary, "Everything");
        Harness.Check("roundtrip.description", evt.Description, "A description with some text");
        Harness.Check("roundtrip.categories", evt.Categories.OrderBy(c => c, StringComparer.Ordinal), new[] { "one", "three", "two" });
        Harness.Check("roundtrip.priority", evt.Priority, 5);
        Harness.Check("roundtrip.sequence", evt.Sequence, 2);
        Harness.Check("roundtrip.status", evt.Status, "CONFIRMED");
        Harness.Check("roundtrip.transparency", evt.Transparency, "OPAQUE");
        Harness.Check("roundtrip.location", evt.Location, "Somewhere");
        Harness.Check("roundtrip.geo", $"{evt.GeographicLocation?.Latitude};{evt.GeographicLocation?.Longitude}", "48.210033;16.363449");
        Harness.Check("roundtrip.url", evt.Url?.ToString(), "https://example.com/");
        Harness.Check("roundtrip.organizer", evt.Organizer?.CommonName, "The Organizer");
        Harness.Check("roundtrip.attendee", evt.Attendees.FirstOrDefault()?.CommonName, "An Attendee");
        Harness.Check("roundtrip.attachment", evt.Attachments.FirstOrDefault()?.Uri?.ToString(), "https://example.com/file.txt");
        Harness.Check("roundtrip.requestStatus", evt.RequestStatuses.FirstOrDefault()?.Description, "Success");
        Harness.Check("roundtrip.alarm.action", evt.Alarms.FirstOrDefault()?.Action, "DISPLAY");
        Harness.Report("roundtrip.alarm.trigger", evt.Alarms.FirstOrDefault()?.Trigger?.Duration?.ToString());
        Harness.Check("roundtrip.todo.summary", calendar.Todos.FirstOrDefault()?.Summary, "A task");
        Harness.Check("roundtrip.journal.summary", calendar.Journals.FirstOrDefault()?.Summary, "A journal entry");
        Harness.Report("roundtrip.rrule", evt.RecurrenceRule?.ToString());
    }

    /// <summary>
    /// Copy on every publicly constructible concrete ICopyable type. Each is listed explicitly
    /// rather than discovered by reflection - reflecting over the assembly is precisely what this
    /// binary must not need to do.
    /// <para/>
    /// The four remaining types (CalendarObject, Period, PeriodList and the private nested
    /// VTimeZone.IntervalRecurrenceRule) are not publicly constructible. They are exercised
    /// through the RDATE/EXDATE and VTIMEZONE fixtures, and directly by CopyAllTypesTests under
    /// the JIT.
    /// </summary>
    internal static void CopyEveryType()
    {
        CheckCopy(new Calendar());
        CheckCopy(new Alarm());
        CheckCopy(new CalendarComponent());
        CheckCopy(new CalendarEvent());
        CheckCopy(new FreeBusy());
        CheckCopy(new Journal());
        CheckCopy(new Todo());
        CheckCopy(new UniqueComponent());
        CheckCopy(new VTimeZone());
        CheckCopy(new CalendarParameter());
        CheckCopy(new CalendarProperty());
        CheckCopy(new VTimeZoneInfo());
        CheckCopy(new Attachment());
        CheckCopy(new Attendee());
        CheckCopy(new EncodableDataType());
        CheckCopy(new FreeBusyEntry());
        CheckCopy(new GeographicLocation());
        CheckCopy(new Organizer());
        CheckCopy(new RecurrenceRule());
        CheckCopy(new RequestStatus());
        CheckCopy(new StatusCode());
        CheckCopy(new Trigger());
        CheckCopy(new UtcOffset());
        CheckCopy(new WeekDay());
    }

    private static void CheckCopy<T>(T original) where T : ICopyable
    {
        var name = typeof(T).FullName!;
        var copy = original.Copy<object>();

        if (copy is null)
        {
            Harness.Fail("copy", $"{name}: Copy returned null");
            Harness.Report($"copy.{name}", "<null>");
            return;
        }

        // The exact runtime type is the invariant: a missing CreateNew() override would either
        // throw here under AOT or silently produce a base type.
        Harness.Check($"copy.{name}", copy.GetType().FullName, name);
    }

    /// <summary>
    /// Copying a populated event, then mutating the copy, must not affect the original.
    /// </summary>
    internal static void CopyIsDeep()
    {
        var original = new CalendarEvent
        {
            Uid = "deep-1",
            Summary = "Original",
            DtStart = new CalDateTime(2026, 5, 1, 9, 0, 0, Tz),
            DtEnd = new CalDateTime(2026, 5, 1, 10, 0, 0, Tz),
            Resources = new[] { "A", "B" },
            GeographicLocation = new GeographicLocation(48.210033, 16.363449),
        };
        original.Attachments.Add(new Attachment("https://original.example.com/"));
        original.Alarms.Add(new Alarm { Action = "DISPLAY", Description = "Original alarm" });

        var copy = original.Copy<CalendarEvent>()!;
        copy.Uid = "deep-2";
        copy.Summary = "Copy";
        copy.Attachments[0].Uri = new Uri("https://copy.example.com/");
        copy.Alarms[0]!.Description = "Copy alarm";

        Harness.Check("deep.original.uid", original.Uid, "deep-1");
        Harness.Check("deep.original.summary", original.Summary, "Original");
        Harness.Check("deep.original.attachment", original.Attachments[0].Uri?.ToString(), "https://original.example.com/");
        Harness.Check("deep.original.alarm", original.Alarms[0]!.Description, "Original alarm");
        Harness.Check("deep.copy.uid", copy.Uid, "deep-2");
        Harness.Check("deep.copy.summary", copy.Summary, "Copy");
        Harness.Check("deep.copy.attachment", copy.Attachments[0].Uri?.ToString(), "https://copy.example.com/");
        Harness.Check("deep.copy.alarm", copy.Alarms[0]!.Description, "Copy alarm");
        Harness.Check("deep.copy.geo", $"{copy.GeographicLocation?.Latitude}", "48.210033");
        Harness.Check("deep.copy.resources", copy.Resources.OrderBy(r => r, StringComparer.Ordinal), new[] { "A", "B" });
    }

    /// <summary>
    /// The four enum-metadata call sites in the library. None of them is annotated
    /// [RequiresUnreferencedCode] or [RequiresDynamicCode] by the BCL, and none of them warns - but
    /// "the analyzer is quiet" is exactly the evidence that proved worthless last time, so exercise
    /// them for real.
    /// <para/>
    /// They are safe because the trimmer never strips fields from an enum type: enum members are
    /// static literal fields, and an enum that survives at all survives whole. The one overload
    /// that genuinely is not AOT-safe, Enum.GetValues(Type), carries [RequiresDynamicCode] because
    /// it has to build an array of the enum type at runtime - the library does not use it.
    /// </summary>
    internal static void EnumParsing()
    {
        // 1. EnumSerializer.Deserialize -> Enum.Parse(Type, string, bool).
        var serializer = new EnumSerializer(typeof(FrequencyType));

        Harness.Check("enum.parse.exact", serializer.Deserialize(new StringReader("Weekly")), FrequencyType.Weekly);
        Harness.Check("enum.parse.ignoreCase", serializer.Deserialize(new StringReader("MONTHLY")), FrequencyType.Monthly);
        Harness.Check("enum.parse.hyphenStripped", serializer.Deserialize(new StringReader("SECOND-LY")), FrequencyType.Secondly);

        // The narrowed catch: an unknown value falls back to the raw string rather than throwing.
        Harness.Check("enum.parse.unknown", serializer.Deserialize(new StringReader("Fortnightly")), "Fortnightly");

        var range = new EnumSerializer(typeof(RecurrenceRange));
        Harness.Check("enum.parse.otherEnum", range.Deserialize(new StringReader("ThisAndFuture")), RecurrenceRange.ThisAndFuture);

        // 2. EnumSerializer.SerializeToString on a boxed enum. This is the path that is actually
        //    reachable through the public API, via PropertySerializer.Build(value.GetType()).
        Harness.Check("enum.serialize", serializer.SerializeToString(FrequencyType.Yearly), "Yearly");

        // 3. RecurrenceRule.Frequency setter -> Enum.IsDefined(Type, object).
        var rule = new RecurrenceRule { Frequency = FrequencyType.Daily };
        Harness.Check("enum.isDefined.accepts", rule.Frequency, FrequencyType.Daily);

        try
        {
            rule.Frequency = (FrequencyType) 999;
            Harness.Fail("enum.isDefined.rejects", "expected ArgumentOutOfRangeException, none thrown");
            Harness.Report("enum.isDefined.rejects", "<no exception>");
        }
        catch (ArgumentOutOfRangeException)
        {
            Harness.Report("enum.isDefined.rejects", "ArgumentOutOfRangeException");
        }

        // 4. RecurrenceRuleSerializer -> Enum.TryParse<FrequencyType>. Covered implicitly by every
        //    RRULE fixture above; assert the parsed value directly as well.
        var calendar = Load(Wrap(
            "BEGIN:VEVENT",
            "UID:enum-1",
            "DTSTART;TZID=" + Tz + ":20260105T090000",
            "DTEND;TZID=" + Tz + ":20260105T100000",
            "RRULE:FREQ=WEEKLY;COUNT=2",
            "SUMMARY:Enum parsing",
            "END:VEVENT"));

        Harness.Check("enum.tryParse.frequency", calendar.Events.First().RecurrenceRule?.Frequency, FrequencyType.Weekly);
    }

    /// <summary>
    /// Services are now registered under explicit types rather than by reflecting over the
    /// interfaces they implement. ISerializerFactory is the only one of the four that is public;
    /// the other three (DataTypeMapper, EncodingStack, IEncodingProvider) are internal and are
    /// covered by the JIT test suite. All four are exercised indirectly by every fixture above,
    /// since serialization fails outright if any of them stops resolving.
    /// </summary>
    internal static void ServiceResolution()
    {
        var ctx = new SerializationContext();

        Harness.Check("service.serializerFactory", ctx.GetService<ISerializerFactory>()?.GetType().Name, "SerializerFactory");
    }
}
