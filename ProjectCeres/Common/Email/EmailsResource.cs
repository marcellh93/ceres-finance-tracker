namespace ProjectCeres;

/// <summary>Marker type for <c>IStringLocalizer&lt;EmailsResource&gt;</c>. The matching
/// resource files are <c>EmailsResource.en.resx</c> and <c>EmailsResource.es.resx</c>
/// under <c>ProjectCeres/Resources/</c>.
///
/// With <c>AddLocalization(o =&gt; o.ResourcesPath = "Resources")</c>, the runtime base name
/// computed by <c>ResourceManagerStringLocalizerFactory</c> is
/// <c>&lt;assembly&gt;.&lt;ResourcesPath&gt;.&lt;TypeFullName-minus-assembly&gt;</c> i.e.
/// <c>ProjectCeres.Resources.EmailsResource</c>. The MSBuild SDK derives the embedded
/// manifest name from the resx file's path under the project root, so
/// <c>Resources/EmailsResource.{culture}.resx</c> produces the same manifest name. Both
/// sides line up only when this marker type lives in the assembly's root namespace AND
/// outside the <c>Resources/</c> folder; placing it inside <c>Resources/</c> would trip
/// the SDK's resx↔.cs auto-pairing and force a different manifest name.</summary>
/// <remarks>
/// The 2026-05-14 implementation plan called for filenames <c>Emails.{culture}.resx</c>
/// and a marker type at <c>ProjectCeres.Resources.EmailsResource</c>. That layout
/// produced a manifest-name mismatch between MSBuild's resx auto-pairing and
/// <c>ResourceManagerStringLocalizerFactory</c>'s lookup at runtime (see commit 53bfe6d
/// for the analysis). Filenames were renamed to match the marker type name, and the
/// marker was relocated to this folder under the root <c>ProjectCeres</c> namespace.
/// </remarks>
public sealed class EmailsResource { }
