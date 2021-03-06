﻿using com.clusterrr.hakchi_gui.Controls;
using com.clusterrr.hakchi_gui.Properties;
using SharpCompress.Archives;
using SharpCompress.Compressors.Deflate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace com.clusterrr.hakchi_gui.Tasks
{
    public class GameTask
    {
        public List<NesApplication> Games
        {
            get; private set;
        }

        public Dictionary<NesApplication, string> GamesChanged
        {
            get; private set;
        }

        public bool ResetAllOriginalGames
        {
            set; get;
        }

        public bool NonDestructiveSync
        {
            set; get;
        }

        public GameTask()
        {
            Games = new List<NesApplication>();
            GamesChanged = new Dictionary<NesApplication, string>();
        }

        public Tasker.Conclusion SetCoverArtForMultipleGames(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.ApplyChanges);
            tasker.SetStatusImage(Resources.sign_file_picture);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.ApplyChanges);

            NesApplication.CachedCoverFiles = null;
            int i = 0, max = GamesChanged.Count;
            foreach (var pair in GamesChanged)
            {
                pair.Key.SetImageFile(pair.Value, ConfigIni.Instance.CompressCover);
                tasker.SetProgress(++i, max);
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion RepairGames(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.RepairGames);
            tasker.SetStatusImage(Resources.sign_cogs);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.RepairGames);

            NesApplication.ParentForm = tasker.HostForm;
            int i = 0, max = Games.Count;
            foreach (var game in Games)
            {
                tasker.SetStatus(string.Format(Resources.RepairingGame, game.Name));
                bool success = game.Repair();
                Trace.WriteLine($"Repairing game \"{game.Name}\" was " + (success ? "successful" : "not successful"));
                tasker.SetProgress(++i, max);
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion ScanCovers(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.ScanningCovers);
            tasker.SetStatusImage(Resources.sign_file_picture);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.ScanningCovers);
            var unknownApps = new List<NesApplication>();
            int i = 0;
            foreach (NesApplication game in Games)
            {
                tasker.SetStatus(string.Format(Resources.ScanningCover, game.Name));
                try
                {
                    uint crc32 = game.Metadata.OriginalCrc32;
                    string gameFile = game.GameFilePath;
                    if (crc32 == 0 && !game.IsOriginalGame && gameFile != null && File.Exists(gameFile))
                    {
                        using (var stream = game.GameFileStream)
                        {
                            if (stream != null)
                            {
                                stream.Position = 0;
                                crc32 = Shared.CRC32(stream);
                                game.Metadata.OriginalCrc32 = crc32;
                                game.SaveMetadata();
                            }
                        }
                    }
                    else
                    {
                        gameFile = game.BasePath;
                    }
                    game.FindCover(game.Metadata.OriginalFilename ?? Path.GetFileName(gameFile), crc32, game.Name);
                    if (!game.CoverArtMatchSuccess && game.CoverArtMatches.Any())
                    {
                        unknownApps.Add(game);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Error trying to finding cover art for game " + game.Name);
                    Trace.WriteLine(ex.Message + ex.StackTrace);
                }
                tasker.SetProgress(++i, Games.Count);
            }

            if (unknownApps.Count > 0)
            {
                tasker.HostForm.Invoke(new Action(() => {
                    using (SelectCoverDialog selectCoverDialog = new SelectCoverDialog())
                    {
                        selectCoverDialog.Games.AddRange(unknownApps);
                        selectCoverDialog.ShowDialog(tasker.HostForm);
                    }
                }));
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion DownloadCovers(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.DownloadAllCoversTitle);
            tasker.SetStatusImage(Resources.sign_globe);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.DownloadAllCoversTitle);
            int i = 0;
            foreach (NesApplication game in Games)
            {
                tasker.SetStatus(Resources.GooglingFor.Trim() + " " + game.Name + "...");
                string[] urls = null;
                for (int tries = 0; tries < 5; tries++)
                {
                    if (urls == null)
                    {
                        try
                        {
                            urls = ImageGooglerForm.GetImageUrls(game);
                            break;
                        }
                        catch (Exception ex)
                        {
                            tasker.SetStatus(Resources.Error + ": " + ex.Message);
                            Thread.Sleep(1500);
                            continue;
                        }
                    }
                }
                if (urls != null && urls.Length == 0)
                    tasker.SetStatus(Resources.NotFound + " " + game.Name);
                for (int tries = 0; urls != null && tries < 5 && tries < urls.Length; tries++)
                {
                    try
                    {
                        var cover = ImageGoogler.DownloadImage(urls[tries]);
                        game.Image = cover;
                        break;
                    }
                    catch (Exception ex)
                    {
                        tasker.SetStatus(Resources.Error + ": " + ex.Message);
                        Thread.Sleep(1500);
                        continue;
                    }
                }
                tasker.SetProgress(++i, Games.Count);
                Thread.Sleep(500); // not so fast, Google don't like it
            }
            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion DeleteCovers(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.RemovingCovers);
            tasker.SetStatusImage(Resources.sign_trashcan);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.RemovingCovers);

            int i = 0;
            foreach (NesApplication game in Games)
            {
                game.Image = null;
                tasker.SetProgress(++i, Games.Count);
            }
            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion CompressGames(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.CompressingGames);
            tasker.SetStatusImage(Resources.sign_cogs);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.CompressingGames);

            int i = 0;
            foreach (NesApplication game in Games)
            {
                if (!game.IsOriginalGame)
                {
                    tasker.SetStatus(string.Format(Resources.Compressing, game.Name));
                    game.Compress();
                    tasker.SetProgress(++i, Games.Count);
                }
                else
                {
                    tasker.SetStatus(string.Format(Resources.Skipping, game.Name));
                }
                Thread.Sleep(1);
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion DecompressGames(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.DecompressingGames);
            tasker.SetStatusImage(Resources.sign_cogs);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.DecompressingGames);

            int i = 0;
            foreach (NesApplication game in Games)
            {
                if (!game.IsOriginalGame)
                {
                    tasker.SetStatus(string.Format(Resources.Decompressing, game.Name));
                    game.Decompress();
                    tasker.SetProgress(++i, Games.Count);
                }
                else
                {
                    tasker.SetStatus(string.Format(Resources.Skipping, game.Name));
                }
                Thread.Sleep(1);
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion DeleteGames(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.RemovingGames);
            tasker.SetStatusImage(Resources.sign_trashcan);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.RemovingGames);

            int i = 0;
            foreach (NesApplication game in Games)
            {
                if (!game.IsOriginalGame)
                {
                    tasker.SetStatus(string.Format(Resources.Removing, game.Name));
                    game.IsDeleting = true;
                    Directory.Delete(game.BasePath, true);
                    tasker.SetProgress(++i, Games.Count);
                }
                else
                {
                    tasker.SetStatus(string.Format(Resources.Skipping, game.Name));
                }
                Thread.Sleep(1);
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion ResetROMHeaders(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.ResettingHeaders);
            tasker.SetStatusImage(Resources.sign_database);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.ResettingHeaders);

            int i = 0;
            foreach (NesApplication game in Games)
            {
                if (game is SnesGame && !(game as SnesGame).IsOriginalGame)
                {
                    tasker.SetStatus(string.Format(Resources.ResettingHeader, game.Name));
                    bool wasCompressed = game.DecompressPossible().Length > 0;
                    if (wasCompressed)
                        game.Decompress();
                    SfromToolWrapper.ResetSFROM(game.GameFilePath);
                    if (wasCompressed)
                        game.Compress();
                }
                else
                {
                    tasker.SetStatus(string.Format(Resources.Skipping, game.Name));
                }

                Thread.Sleep(1);
                tasker.SetProgress(++i, Games.Count);
            }

            return Tasker.Conclusion.Success;
        }

        public Tasker.Conclusion SyncOriginalGames(Tasker tasker, Object SyncObject = null)
        {
            tasker.SetTitle(Resources.ResettingOriginalGames);
            tasker.SetStatusImage(Resources.sign_sync);
            tasker.SetProgress(-1, -1, Tasker.State.Running, Resources.ResettingOriginalGames);

            string desktopEntriesArchiveFile = Path.Combine(Path.Combine(Program.BaseDirectoryInternal, "data"), "desktop_entries.tar");
            string originalGamesPath = Path.Combine(Program.BaseDirectoryExternal, "games_originals");

            if (!Directory.Exists(originalGamesPath))
                Directory.CreateDirectory(originalGamesPath);

            if (!File.Exists(desktopEntriesArchiveFile))
                throw new FileLoadException("desktop_entries.tar data file was deleted, cannot sync original games.");

            try
            {
                var defaultGames = ResetAllOriginalGames ? NesApplication.AllDefaultGames.Select(g => g.Key) : NesApplication.CurrentDefaultGames;
                using (var extractor = ArchiveFactory.Open(desktopEntriesArchiveFile))
                using (var reader = extractor.ExtractAllEntries())
                {
                    int i = 0;
                    while (reader.MoveToNextEntry())
                    {
                        if (reader.Entry.IsDirectory)
                            continue;

                        var code = Path.GetFileNameWithoutExtension(reader.Entry.Key);
                        if (!defaultGames.Contains(code))
                            continue;

                        var ext = Path.GetExtension(reader.Entry.Key).ToLower();
                        if (ext != ".desktop") // sanity check
                            throw new FileLoadException($"invalid file \"{reader.Entry.Key}\" found in desktop_entries.tar data file.");

                        string path = Path.Combine(originalGamesPath, code);
                        string outputFile = Path.Combine(path, code + ".desktop");
                        bool exists = File.Exists(outputFile);

                        if (exists && !NonDestructiveSync)
                        {
                            Shared.EnsureEmptyDirectory(path);
                            Thread.Sleep(0);
                        }

                        if (!exists || !NonDestructiveSync)
                        {
                            Directory.CreateDirectory(path);

                            // extract .desktop file from archive
                            using (var o = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                            {
                                reader.WriteEntryTo(o);
                                o.Flush();
                                if (!this.ResetAllOriginalGames && !ConfigIni.Instance.OriginalGames.Contains(code))
                                {
                                    ConfigIni.Instance.OriginalGames.Add(code);
                                }
                            }

                            // create game temporarily to perform cover search
                            Trace.WriteLine(string.Format($"Resetting game \"{NesApplication.AllDefaultGames[code].Name}\"."));
                            var game = NesApplication.FromDirectory(path);
                            game.FindCover(code + ".desktop");
                            game.Save();
                        }

                        tasker.SetProgress(++i, defaultGames.Count());
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error synchronizing original games " + ex.Message + ex.StackTrace);
                tasker.ShowError(ex, Resources.ErrorRestoringAllOriginalGames);
                return Tasker.Conclusion.Error;
            }

            return Tasker.Conclusion.Success;
        }

        internal Tasker.Conclusion ArchiveGames(Tasker tasker, object syncObject)
        {
            long counter = 0;
            long gameCount = Games.Count;

            tasker.SetTitle(gameCount > 1 ? Resources.ArchivingGames : Resources.ArchivingGame);
            
            string directory = null;

            if (gameCount > 1)
            {
                using (var fbd = new FolderBrowserDialog() { SelectedPath = Program.BaseDirectoryExternal })
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        directory = fbd.SelectedPath;
                    }
                    else
                    {
                        return Tasker.Conclusion.Abort;
                    }
                }
            }

            foreach (var game in Games)
            {
                tasker.SetStatus(String.Format(Resources.Archiving, game.Name));
                var fileName = Shared.ReplaceInvalidFilenameCharacters($"{game.Code} - {game.Name}.clvg");

                if (directory == null)
                {
                    using (var sfd = new SaveFileDialog() { Filter = Resources.GameArchive + "(*.clvg)|*.clvg", FileName = fileName })
                    {
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            game.Archive(sfd.FileName);
                        }
                    }
                }
                else
                {
                    var archivePath = Path.Combine(directory, fileName);
                    if (File.Exists(archivePath) &&  tasker.ShowMessage(Resources.ReplaceFileQ, String.Format(Resources.ReplaceFollowingFileQ, archivePath), Resources.sign_question, new MessageForm.Button[] { MessageForm.Button.Yes, MessageForm.Button.No }) == MessageForm.Button.No)
                    {
                        continue;
                    }
                    game.Archive(archivePath);
                }
                counter++;
                tasker.SetProgress(counter, gameCount);
            }
            return Tasker.Conclusion.Success;
        }
    }
}
