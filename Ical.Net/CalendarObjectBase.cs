//
// Copyright ical.net project maintainers and contributors.
// Licensed under the MIT license.
//

using System;
using System.Diagnostics.CodeAnalysis;

namespace Ical.Net;

public abstract class CalendarObjectBase : ICopyable
{
    /// <summary>
    /// Makes a deep copy of the <see cref="ICopyable"/> source
    /// to the current object. This method must be overridden in a derived class.
    /// </summary>
    public virtual void CopyFrom(ICopyable obj)
        => throw new NotImplementedException("Must be implemented in a derived class.");

    /// <summary>
    /// Creates a new, empty instance of the concrete runtime type - a "virtual constructor".
    /// </summary>
    /// <remarks>
    /// Every concrete type in this library overrides this with a direct <c>new</c>, which is what
    /// lets <see cref="Copy{T}"/> avoid reflection and stay trimming- and NativeAOT-safe.
    /// <para/>
    /// The base implementation exists only so that introducing this member is not a breaking
    /// change for subclasses outside this library. It falls back to <see cref="Activator"/> and
    /// therefore needs the concrete type's parameterless constructor to survive trimming; under
    /// NativeAOT a subclass relying on the fallback fails loudly with a
    /// <see cref="MissingMethodException"/>. Override it.
    /// </remarks>
    /// <returns>A new instance of the runtime type of this object.</returns>
    protected virtual CalendarObjectBase? CreateNew() => CreateNewByActivator();

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Fallback for subclasses outside this library that do not override CreateNew(). "
            + "Every type in this library overrides it, so the trimmer is never asked to preserve a "
            + "library type's constructor on account of this call. A subclass that relies on the "
            + "fallback under trimming or NativeAOT fails loudly rather than silently.")]
    private CalendarObjectBase? CreateNewByActivator()
        => Activator.CreateInstance(GetType(), true) as CalendarObjectBase;

    /// <summary>
    /// Creates a deep copy of the <see cref="T"/> object.
    /// </summary>
    /// <returns>The copy of the <see cref="T"/> object.</returns>
    public virtual T? Copy<T>()
    {
        var obj = CreateNew();

        if (obj is not T objOfT) return default(T?);

        ((ICopyable) obj).CopyFrom(this);
        return objOfT;
    }
}
