using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// RSA key pair used to sign approved catalog packages. Keys are stored as JSON
    /// (base64 RSA parameters) so they work the same on net48 and .NET 8.
    /// The private key stays on administrator PCs; the public key is embedded in the apps.
    /// </summary>
    public sealed class CatalogKey
    {
        private const int KeySizeBits = 3072;

        private CatalogKey(RSAParameters parameters, bool hasPrivate)
        {
            Parameters = parameters;
            HasPrivateKey = hasPrivate;
            KeyId = ComputeKeyId(parameters.Modulus!, parameters.Exponent!);
        }

        public RSAParameters Parameters { get; }
        public bool HasPrivateKey { get; }

        /// <summary>Short fingerprint of the public key (first 16 hex chars of SHA-256).</summary>
        public string KeyId { get; }

        public static CatalogKey Generate()
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.KeySize = KeySizeBits;
                return new CatalogKey(rsa.ExportParameters(true), true);
            }
        }

        public CatalogKey PublicOnly() =>
            new CatalogKey(new RSAParameters { Modulus = Parameters.Modulus, Exponent = Parameters.Exponent }, false);

        public string ToJson()
        {
            var data = new Dictionary<string, string>
            {
                ["kind"] = HasPrivateKey ? "private" : "public",
                ["keyId"] = KeyId,
                ["modulus"] = B64(Parameters.Modulus),
                ["exponent"] = B64(Parameters.Exponent),
            };
            if (HasPrivateKey)
            {
                data["d"] = B64(Parameters.D);
                data["p"] = B64(Parameters.P);
                data["q"] = B64(Parameters.Q);
                data["dp"] = B64(Parameters.DP);
                data["dq"] = B64(Parameters.DQ);
                data["inverseQ"] = B64(Parameters.InverseQ);
            }
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }

        public static CatalogKey FromJson(string json)
        {
            Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data == null || !data.ContainsKey("modulus") || !data.ContainsKey("exponent"))
                throw new InvalidDataException("Not a catalog key file (modulus/exponent missing).");

            bool hasPrivate = data.ContainsKey("d");
            var parameters = new RSAParameters
            {
                Modulus = Convert.FromBase64String(data["modulus"]),
                Exponent = Convert.FromBase64String(data["exponent"]),
            };
            if (hasPrivate)
            {
                parameters.D = Convert.FromBase64String(data["d"]);
                parameters.P = Convert.FromBase64String(data["p"]);
                parameters.Q = Convert.FromBase64String(data["q"]);
                parameters.DP = Convert.FromBase64String(data["dp"]);
                parameters.DQ = Convert.FromBase64String(data["dq"]);
                parameters.InverseQ = Convert.FromBase64String(data["inverseQ"]);
            }
            return new CatalogKey(parameters, hasPrivate);
        }

        public static CatalogKey Load(string path) => FromJson(File.ReadAllText(path));

        /// <summary>
        /// The trusted public key compiled into TiaOpenness.Core (resource
        /// <c>Security/catalog-public-key.json</c>), or null when the build has none.
        /// </summary>
        public static CatalogKey? Embedded
        {
            get
            {
                Assembly assembly = typeof(CatalogKey).Assembly;
                string? name = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("catalog-public-key.json", StringComparison.OrdinalIgnoreCase));
                if (name == null)
                    return null;
                using (Stream? stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null)
                        return null;
                    using (var reader = new StreamReader(stream))
                        return FromJson(reader.ReadToEnd()).PublicOnly();
                }
            }
        }

        public byte[] Sign(byte[] data)
        {
            if (!HasPrivateKey)
                throw new InvalidOperationException("Signing requires the private catalog key.");
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(Parameters);
                return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        public bool Verify(byte[] data, byte[] signature)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(new RSAParameters { Modulus = Parameters.Modulus, Exponent = Parameters.Exponent });
                return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        private static string ComputeKeyId(byte[] modulus, byte[] exponent)
        {
            using (SHA256 sha = SHA256.Create())
                return ApprovedCatalog.Hex(sha.ComputeHash(modulus.Concat(exponent).ToArray())).Substring(0, 16);
        }

        private static string B64(byte[]? value) => value == null ? string.Empty : Convert.ToBase64String(value);
    }

    /// <summary>One file of a catalog package with its SHA-256.</summary>
    public sealed class CatalogManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
    }

    /// <summary>manifest.json of a catalog package; catalog.sig signs its exact bytes.</summary>
    public sealed class CatalogManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string CatalogVersion { get; set; } = string.Empty;
        public string PublishedUtc { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public List<CatalogManifestFile> Files { get; set; } = new List<CatalogManifestFile>();
    }

    public sealed class CatalogVerification
    {
        public bool IsValid => Errors.Count == 0;
        public string PackageDir { get; set; } = string.Empty;
        public CatalogManifest? Manifest { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Approved catalog package: a folder <c>catalog-vNN</c> with <c>catalog.db</c>, optional
    /// approved files (templates, block library), <c>manifest.json</c> (SHA-256 of every file)
    /// and <c>catalog.sig</c> (RSA-SHA256 signature of manifest.json).
    /// A share root holds several packages plus <c>current.txt</c> naming the current one.
    /// </summary>
    public sealed class ApprovedCatalog
    {
        public const string ManifestFileName = "manifest.json";
        public const string SignatureFileName = "catalog.sig";
        public const string DatabaseFileName = "catalog.db";
        public const string CurrentFileName = "current.txt";
        public const string PackagePrefix = "catalog-v";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private ApprovedCatalog(CatalogManifest manifest, string packageDir, List<CatalogEntry> types)
        {
            Manifest = manifest;
            PackageDir = packageDir;
            Types = types;
        }

        public CatalogManifest Manifest { get; }
        public string PackageDir { get; }
        public string Version => Manifest.CatalogVersion;
        public List<CatalogEntry> Types { get; }

        // ------------------------------------------------------------------ publish (admin)

        /// <summary>
        /// Creates a signed package <c>shareRoot\catalog-v{version}</c> from the approved types and
        /// the contents of <paramref name="extraFilesDir"/> (templates, blocks), then points
        /// <c>current.txt</c> at it. An existing version is never overwritten.
        /// </summary>
        public static string Publish(IReadOnlyList<CatalogEntry> types, string version, string shareRoot,
            CatalogKey privateKey, string publisher, string? extraFilesDir = null, bool makeCurrent = true)
        {
            if (!IsValidVersion(version))
                throw new ArgumentException($"Invalid catalog version '{version}' (letters, digits, '.', '-', '_').");
            if (!privateKey.HasPrivateKey)
                throw new InvalidOperationException("Publishing requires the private catalog key.");

            string packageDir = Path.Combine(Path.GetFullPath(shareRoot), PackagePrefix + version);
            if (Directory.Exists(packageDir))
                throw new IOException($"Catalog version already exists: {packageDir}");

            // Build in a temporary sibling folder and rename at the end, so readers never see a half-written package.
            string staging = packageDir + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(staging);
            try
            {
                if (!string.IsNullOrWhiteSpace(extraFilesDir))
                    CopyDirectory(extraFilesDir!, staging);

                WriteDatabase(Path.Combine(staging, DatabaseFileName), types, version, publisher);

                var manifest = new CatalogManifest
                {
                    CatalogVersion = version,
                    PublishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Publisher = publisher,
                    KeyId = privateKey.KeyId,
                    Files = EnumeratePackageFiles(staging)
                        .Select(rel => new CatalogManifestFile
                        {
                            Path = rel,
                            Sha256 = HashFile(Path.Combine(staging, rel)),
                            Size = new FileInfo(Path.Combine(staging, rel)).Length,
                        })
                        .ToList(),
                };

                byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions));
                File.WriteAllBytes(Path.Combine(staging, ManifestFileName), manifestBytes);
                File.WriteAllText(Path.Combine(staging, SignatureFileName), Convert.ToBase64String(privateKey.Sign(manifestBytes)));

                Directory.Move(staging, packageDir);
            }
            catch
            {
                try { Directory.Delete(staging, true); } catch { /* best effort */ }
                throw;
            }

            if (makeCurrent)
                File.WriteAllText(Path.Combine(Path.GetFullPath(shareRoot), CurrentFileName), PackagePrefix + version);

            return packageDir;
        }

        // ------------------------------------------------------------------ verify / load

        /// <summary>Resolves a share root (via current.txt) or a package folder to the package folder.</summary>
        public static string ResolvePackageDir(string shareRootOrPackage, string? version = null)
        {
            string path = Path.GetFullPath(shareRootOrPackage);
            if (File.Exists(Path.Combine(path, ManifestFileName)))
                return path;

            if (!string.IsNullOrWhiteSpace(version))
                return Path.Combine(path, PackagePrefix + version);

            string current = Path.Combine(path, CurrentFileName);
            if (!File.Exists(current))
                throw new FileNotFoundException($"No {ManifestFileName} or {CurrentFileName} in '{path}'.", current);

            string name = File.ReadAllText(current).Trim();
            if (!name.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException($"{CurrentFileName} must name a '{PackagePrefix}…' folder, found '{name}'.");
            return Path.Combine(path, name);
        }

        /// <summary>Checks signature, key, file hashes and that no unlisted files exist.</summary>
        public static CatalogVerification Verify(string packageDir, CatalogKey? trustedKey = null)
        {
            var result = new CatalogVerification { PackageDir = Path.GetFullPath(packageDir) };
            VerifyCore(result, trustedKey, out _);
            return result;
        }

        /// <summary>
        /// Verifies the package and loads the approved types. catalog.db is read from the same bytes
        /// that were hashed (copied to a temp file), so a file swapped after verification is never used.
        /// Throws <see cref="InvalidDataException"/> with all reasons if the package is not valid.
        /// </summary>
        public static ApprovedCatalog Load(string shareRootOrPackage, CatalogKey? trustedKey = null, string? version = null)
        {
            var result = new CatalogVerification { PackageDir = ResolvePackageDir(shareRootOrPackage, version) };
            VerifyCore(result, trustedKey, out byte[]? databaseBytes);
            if (!result.IsValid)
                throw new InvalidDataException("Approved catalog is not valid:" + Environment.NewLine + "  " +
                                               string.Join(Environment.NewLine + "  ", result.Errors));
            if (databaseBytes == null)
                throw new InvalidDataException($"Approved catalog has no {DatabaseFileName}.");

            string temp = Path.Combine(Path.GetTempPath(), $"tia_catalog_{Guid.NewGuid():N}.db");
            try
            {
                File.WriteAllBytes(temp, databaseBytes);
                List<CatalogEntry> types = ReadTypes(temp);
                return new ApprovedCatalog(result.Manifest!, result.PackageDir, types);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }

        public CatalogEntry? FindType(string? typeKey) =>
            string.IsNullOrWhiteSpace(typeKey)
                ? null
                : Types.FirstOrDefault(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));

        private static void VerifyCore(CatalogVerification result, CatalogKey? trustedKey, out byte[]? databaseBytes)
        {
            databaseBytes = null;
            string dir = result.PackageDir;
            CatalogKey? key = trustedKey ?? CatalogKey.Embedded;
            if (key == null)
            {
                result.Errors.Add("No trusted catalog public key is built into this application.");
                return;
            }
            if (!Directory.Exists(dir))
            {
                result.Errors.Add($"Catalog package not found: {dir}");
                return;
            }

            string manifestPath = Path.Combine(dir, ManifestFileName);
            string signaturePath = Path.Combine(dir, SignatureFileName);
            if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            {
                result.Errors.Add($"{ManifestFileName} or {SignatureFileName} missing in {dir}.");
                return;
            }

            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());
            }
            catch (FormatException)
            {
                result.Errors.Add($"{SignatureFileName} is not valid base64.");
                return;
            }

            if (!key.Verify(manifestBytes, signature))
            {
                result.Errors.Add("Signature is not valid for the trusted catalog key (manifest changed or signed with another key).");
                return;
            }

            CatalogManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<CatalogManifest>(manifestBytes, JsonOptions);
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"{ManifestFileName} is not valid: {ex.Message}");
                return;
            }
            if (manifest == null)
            {
                result.Errors.Add($"{ManifestFileName} is empty.");
                return;
            }
            result.Manifest = manifest;

            if (!string.Equals(manifest.KeyId, key.KeyId, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add($"Manifest key {manifest.KeyId} differs from the trusted key {key.KeyId}.");

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogManifestFile file in manifest.Files)
            {
                listed.Add(file.Path);
                string full = Path.GetFullPath(Path.Combine(dir, file.Path));
                if (!full.StartsWith(dir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Manifest path escapes the package: {file.Path}");
                    continue;
                }
                if (!File.Exists(full))
                {
                    result.Errors.Add($"Missing file: {file.Path}");
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(full);
                if (!string.Equals(HashBytes(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Modified file: {file.Path}");
                    continue;
                }
                if (string.Equals(file.Path, DatabaseFileName, StringComparison.OrdinalIgnoreCase))
                    databaseBytes = bytes;
            }

            foreach (string extra in EnumeratePackageFiles(dir).Where(f => !listed.Contains(f)))
                result.Errors.Add($"Unexpected file (not signed): {extra}");

            if (!listed.Contains(DatabaseFileName))
                result.Errors.Add($"{DatabaseFileName} is not part of the signed manifest.");
        }

        // ------------------------------------------------------------------ catalog.db

        private static void WriteDatabase(string path, IReadOnlyList<CatalogEntry> types, string version, string publisher)
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using (SqliteTransaction transaction = connection.BeginTransaction())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
CREATE TABLE catalog_info (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE types (
    type_key    TEXT PRIMARY KEY COLLATE NOCASE,
    mlfb        TEXT,
    tia_version TEXT,
    description TEXT,
    role        TEXT NOT NULL CHECK (role IN ('Root', 'Module')),
    identifier  TEXT
);
PRAGMA user_version = 1;";
                    command.ExecuteNonQuery();

                    command.CommandText = "INSERT INTO catalog_info (key, value) VALUES (@k, @v);";
                    command.Parameters.Add(new SqliteParameter("@k", string.Empty));
                    command.Parameters.Add(new SqliteParameter("@v", string.Empty));
                    foreach (var (k, v) in new[] { ("catalog_version", version), ("publisher", publisher) })
                    {
                        command.Parameters["@k"].Value = k;
                        command.Parameters["@v"].Value = v;
                        command.ExecuteNonQuery();
                    }

                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO types (type_key, mlfb, tia_version, description, role, identifier) " +
                                          "VALUES (@type_key, @mlfb, @tia_version, @description, @role, @identifier);";
                    foreach (string p in new[] { "@type_key", "@mlfb", "@tia_version", "@description", "@role", "@identifier" })
                        command.Parameters.Add(new SqliteParameter(p, DBNull.Value));
                    foreach (CatalogEntry t in types)
                    {
                        command.Parameters["@type_key"].Value = t.TypeKey;
                        command.Parameters["@mlfb"].Value = t.Mlfb;
                        command.Parameters["@tia_version"].Value = t.TiaVersion;
                        command.Parameters["@description"].Value = t.Description;
                        command.Parameters["@role"].Value = t.Role.ToString();
                        command.Parameters["@identifier"].Value = t.Identifier;
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        private static List<CatalogEntry> ReadTypes(string path)
        {
            var types = new List<CatalogEntry>();
            using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT type_key, mlfb, tia_version, description, role, identifier FROM types ORDER BY type_key;";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            types.Add(new CatalogEntry
                            {
                                TypeKey = reader.GetString(0),
                                Mlfb = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                TiaVersion = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                Role = string.Equals(reader.GetString(4), "Root", StringComparison.OrdinalIgnoreCase)
                                    ? CatalogItemRole.Root
                                    : CatalogItemRole.Module,
                                Identifier = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                            });
                        }
                    }
                }
            }
            return types;
        }

        // ------------------------------------------------------------------ helpers

        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(bytes));
        }

        private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));

        private static bool IsValidVersion(string version) =>
            !string.IsNullOrWhiteSpace(version) && version.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');

        /// <summary>Package files as relative '/'-separated paths, excluding manifest and signature, sorted.</summary>
        private static List<string> EnumeratePackageFiles(string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length).Replace('\\', '/'))
                .Where(f => !string.Equals(f, ManifestFileName, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(f, SignatureFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        private static void CopyDirectory(string source, string target)
        {
            string root = Path.GetFullPath(source).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, file.Substring(root.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
    }
}
