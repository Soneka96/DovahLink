using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DovahLink.Host.PairingCeremony;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// E-08: keeps the dormant sas-pairing integration below its boundary. The Host never references the
/// package, the integration, or the test peer; the integration never references the Host, so it cannot
/// reach trust, pairing policy, or sessions; native and socket types never appear outside the
/// integration's native port; and nothing in production approves a SAS or drives outside the owner.
/// </summary>
public sealed partial class PairingCeremonyArchitectureTests
{
    /// <summary>The Host assembly.</summary>
    private static readonly Assembly HostAssembly = typeof(DovahLink.Host.Constants).Assembly;

    /// <summary>The dormant integration assembly.</summary>
    private static readonly Assembly CeremonyAssembly = typeof(PairingCeremonyHost).Assembly;

    /// <summary>Verifies the running Host references neither the package, the integration, nor the test peer.</summary>
    [Fact]
    public void HostAssembly_ReferencesNoSasPairingAssembly()
    {
        string[] references = [.. HostAssembly.GetReferencedAssemblies().Select(name => name.Name!)];

        Assert.DoesNotContain("SasPairing", references);
        Assert.DoesNotContain("DovahLink.Host.PairingCeremony", references);
        Assert.DoesNotContain("DovahLink.Host.PairingCeremony.TestPeer", references);
    }

    /// <summary>Verifies the integration depends on the package but never on the Host, so it cannot reach trust or pairing policy.</summary>
    [Fact]
    public void CeremonyAssembly_ReferencesPackageButNotHost()
    {
        string[] references = [.. CeremonyAssembly.GetReferencedAssemblies().Select(name => name.Name!)];

        Assert.Contains("SasPairing", references);
        Assert.DoesNotContain("DovahLink.Host", references);
        Assert.DoesNotContain("DovahLink.Host.PairingCeremony.TestPeer", references);
    }

    /// <summary>Verifies no Host trust, pairing, session, security, identity, or protocol type uses a socket or safe handle.</summary>
    [Fact]
    public void HostDomainTypes_UseNoSocketOrSafeHandle()
    {
        string[] domainNamespaces =
        [
            "DovahLink.Host.Trust", "DovahLink.Host.Pairing", "DovahLink.Host.Sessions", "DovahLink.Host.Security",
            "DovahLink.Host.Identity", "DovahLink.Host.Client.Protocol",
        ];
        IEnumerable<Type> domainTypes = HostAssembly.GetTypes().Where(type =>
            domainNamespaces.Any(ns => type.Namespace == ns || type.Namespace?.StartsWith(ns + ".", StringComparison.Ordinal) == true));

        foreach (Type type in domainTypes)
        {
            foreach (Type used in SignatureTypes(type))
            {
                Assert.False(
                    used == typeof(Socket) || typeof(SafeHandle).IsAssignableFrom(used),
                    $"{type.FullName} uses {used.FullName}.");
            }
        }
    }

    /// <summary>
    /// Verifies package and socket types appear only in the integration's native port and owner: every
    /// other integration type, and the whole public surface, holds detached values only.
    /// </summary>
    [Fact]
    public void CeremonyTypes_KeepPackageAndSocketTypesInsideNativePort()
    {
        foreach (Type type in CeremonyAssembly.GetTypes())
        {
            // Public surfaces expose detached values only; internal types outside the native port use no
            // package or socket type at all.
            bool insideNativePort = !type.IsPublic && type.Namespace == "DovahLink.Host.PairingCeremony.Native";
            if (insideNativePort)
            {
                continue;
            }

            foreach (Type usedType in type.IsPublic ? PublicSignatureTypes(type) : SignatureTypes(type))
            {
                Assert.False(usedType.Namespace == "SasPairing", $"{type.FullName} uses package type {usedType.FullName}.");
                Assert.False(usedType == typeof(Socket) || typeof(SafeHandle).IsAssignableFrom(usedType), $"{type.FullName} uses {usedType.FullName}.");
            }
        }
    }

    /// <summary>Verifies no Host production source names the integration, the package, or its native library.</summary>
    [Fact]
    public void HostProductionSources_NeverComposeTheIntegration()
    {
        foreach (string path in ProductionSources("host/DovahLink.Host"))
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotMatch(SasPairingNamePattern(), source);
        }
    }

    /// <summary>
    /// Verifies that in production code only the ceremony owner drives the native session and applies
    /// SAS approval, and that nothing in production submits a SAS decision on its own.
    /// </summary>
    [Fact]
    public void CeremonyProductionSources_DriveAndApproveOnlyInTheOwner()
    {
        foreach (string path in ProductionSources("host/DovahLink.Host.PairingCeremony"))
        {
            string file = Path.GetFileName(path);
            string source = File.ReadAllText(path);
            bool owner = file == "PairingCeremonyHost.cs";
            bool port = file == "SasPairingNativeSession.cs";
            if (!owner && !port)
            {
                Assert.DoesNotMatch(@"\.Drive\(\)", source);
                Assert.DoesNotMatch(@"\.ApproveSas\(", source);
            }

            // Any call, qualified or not; only the interface and implementation declarations name it.
            Assert.DoesNotMatch(@"(?<!bool )TrySubmitSasDecision\(", source);
        }
    }

    /// <summary>Returns the types a type's fields, properties, method parameters and returns, and constructor parameters use.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The used types, unwrapped from arrays, by-refs, nullables, and generic arguments.</returns>
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        IEnumerable<Type> direct = type.GetFields(All).Select(field => field.FieldType)
            .Concat(type.GetProperties(All).Select(property => property.PropertyType))
            .Concat(type.GetMethods(All).SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)))
            .Concat(type.GetConstructors(All).SelectMany(ctor => ctor.GetParameters().Select(p => p.ParameterType)));
        return direct.SelectMany(Unwrap);
    }

    /// <summary>Returns the types a public type's public members use.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The used types.</returns>
    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        const BindingFlags Public = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
        IEnumerable<Type> direct = type.GetFields(Public).Select(field => field.FieldType)
            .Concat(type.GetProperties(Public).Select(property => property.PropertyType))
            .Concat(type.GetMethods(Public).SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)))
            .Concat(type.GetConstructors(Public).SelectMany(ctor => ctor.GetParameters().Select(p => p.ParameterType)));
        return direct.SelectMany(Unwrap);
    }

    /// <summary>Unwraps arrays, by-refs, and generic arguments into the types they contain.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The type and every type it contains.</returns>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (Type element in Unwrap(type.GetElementType()!))
            {
                yield return element;
            }
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments().SelectMany(Unwrap))
            {
                yield return argument;
            }
        }
    }

    /// <summary>Lists a project's handwritten C# sources, excluding build output.</summary>
    /// <param name="projectDirectory">The project directory, relative to the repository root.</param>
    /// <returns>The source paths.</returns>
    private static IEnumerable<string> ProductionSources(string projectDirectory)
    {
        string root = Path.Combine(SasPairingTestArtifacts.RepositoryRoot, projectDirectory);
        string bin = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        string obj = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        string[] sources = [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains(bin) && !path.Contains(obj))];
        Assert.NotEmpty(sources);
        return sources;
    }

    /// <summary>Matches any mention of the sas-pairing package, the integration project, or its native library.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"SasPairing|PairingCeremony|sas_pairing_core", RegexOptions.CultureInvariant)]
    private static partial Regex SasPairingNamePattern();
}
