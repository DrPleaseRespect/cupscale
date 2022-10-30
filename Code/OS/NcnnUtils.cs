﻿using Cupscale.Forms;
using Cupscale.ImageUtils;
using Cupscale.Implementations;
using Cupscale.IO;
using Cupscale.UI;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cupscale.OS
{
    class NcnnUtils
    {
		public static string currentNcnnModel;
        public static string lastNcnnOutput;
		public static string lastScaleCheckOutput;
		static Process currentProcess;
		static string ncnnDir = "";
		private static string ConverterDir { get => Path.Combine(Paths.binPath, "pth2ncnn"); }

		public static async Task ConvertNcnnModel(string modelPath, string filenamePattern)
        {
			Logger.Log($"ConvertNcnnModel: {modelPath}");

            try
            {
                if (IsDirNcnnModel(modelPath))
                {
					ApplyFilenamePattern(modelPath, filenamePattern);
					currentNcnnModel = modelPath;
					return;
				}

                string modelName = Path.GetFileName(modelPath);
                ncnnDir = Path.Combine(Config.Get("modelPath"), ".ncnn-models");
                Directory.CreateDirectory(ncnnDir);
                string outPath = Path.Combine(ncnnDir, Path.ChangeExtension(modelName, null));
                Logger.Log("Checking for NCNN model: " + outPath);

                if (IoUtils.GetAmountOfFiles(outPath, false) < 2)
                {
                    Logger.Log("Running model converter...");
                    DialogForm dialog = new DialogForm("Converting ESRGAN model to NCNN format...");
                    await RunConverter(modelPath, outPath);

                    if (lastNcnnOutput.Contains("Error:"))
                        throw new Exception(lastNcnnOutput.SplitIntoLines().Where(x => x.Contains("Error:")).First());

                    dialog.Close();
                }
                else
                {
                    Logger.Log("NCNN Model is cached - Skipping conversion.");
                }

				ApplyFilenamePattern(outPath, filenamePattern);
                currentNcnnModel = outPath;
            }
            catch (Exception e)
            {
				Logger.ErrorMessage("Failed to convert Pytorch model to NCNN format! It might be incompatible.", e);
            }
        }

		static void ApplyFilenamePattern(string path, string pattern)
        {
			foreach (FileInfo file in IoUtils.GetFileInfosSorted(path).Where(f => f.Extension == ".bin" || f.Extension == ".param"))
				IoUtils.RenameFile(file.FullName, pattern.Replace("*", $"{file.Name.GetInt()}"));
		}

		static async Task RunConverter(string modelPath, string directory)
        {
            lastNcnnOutput = "";
			bool showWindow = Config.GetInt("cmdDebugMode") > 0;
			bool stayOpen = Config.GetInt("cmdDebugMode") == 2;

			modelPath = modelPath.Wrap();
			directory = directory.Wrap();

			string opt = "/C";
			if (stayOpen) opt = "/K";

			string args = $"{opt} cd /D {ConverterDir.Wrap()} & {EmbeddedPython.GetPyCmd()} pth2ncnn.py {modelPath} --outpath {directory}";

			Logger.Log("[CMD] " + args);
			Process converterProc = OsUtils.NewProcess(!showWindow);
			converterProc.StartInfo.Arguments = args;

			if (!showWindow)
			{
				converterProc.OutputDataReceived += ConverterOutputHandler;
				converterProc.ErrorDataReceived += ConverterOutputHandler;
			}

			currentProcess = converterProc;
			converterProc.Start();

			if (!showWindow)
			{
				converterProc.BeginOutputReadLine();
				converterProc.BeginErrorReadLine();
			}

			while (!converterProc.HasExited)
				await Task.Delay(100);
		}

		private static void ConverterOutputHandler(object sendingProcess, DataReceivedEventArgs output)
		{
			if (output == null || output.Data == null)
				return;

			string data = output.Data;
			Logger.Log("[NcnnUtils] Model Converter Output: " + data);
            lastNcnnOutput += $"{data}\n";
        }

		public static bool IsDirNcnnModel (string path, bool requireSuffix = true, bool noMoreThanTwoFiles = false)
        {
            try
            {
				if (!IoUtils.IsPathDirectory(path))
					return false;

				DirectoryInfo dir = new DirectoryInfo(path);
				bool suffixValid = dir.Name.EndsWith(".ncnn");
				bool filesValid = dir.GetFiles("*.bin").Length == 1 && dir.GetFiles("*.param").Length == 1;

				if (noMoreThanTwoFiles && dir.GetFiles("*").Length > 2)
					filesValid = false;

				if (requireSuffix && !suffixValid)
					return false;

				return filesValid;
			}
			catch(Exception e)
            {
				Logger.Log($"IsDirNcnnModel Exception: {e.Message}. Defaulting to false.");
				return false;
            }
		}

		static async Task RunScaleCheck(string bin, string param)
		{
			lastScaleCheckOutput = "";

			bin = bin.Wrap();
			param = param.Wrap();

			string opt = "/C";

			string args = $"{opt} cd /D {ConverterDir.Wrap()} & {EmbeddedPython.GetPyCmd()} get_scale.py {bin} {param}";

			Logger.Log("[CMD] " + args);
			Process converterProc = OsUtils.NewProcess(true);
			converterProc.StartInfo.Arguments = args;


			converterProc.OutputDataReceived += ScaleCheckOutputHandler;
			converterProc.ErrorDataReceived += ScaleCheckOutputHandler;

			currentProcess = converterProc;
			converterProc.Start();


			converterProc.BeginOutputReadLine();
			converterProc.BeginErrorReadLine();

			while (!converterProc.HasExited)
				await Task.Delay(100);
		}

		private static void ScaleCheckOutputHandler(object sendingProcess, DataReceivedEventArgs output)
		{
			if (output == null || output.Data == null)
				return;

			string data = output.Data;
			Logger.Log("[NcnnUtils] Scale Check Output: " + data);
			lastScaleCheckOutput += $"{data}\n";
		}

		public static async Task<int> GetNcnnModelScale(string modelDir)
		{
            try
            {
				string bin_file = Directory.GetFiles(modelDir, "*.bin")[0];
				string param_file = Directory.GetFiles(modelDir, "*.param")[0];
				await RunScaleCheck(bin_file, param_file);
				return lastScaleCheckOutput.GetInt();
			}
			catch (Exception e)
            {
				Logger.Log($"Failed to get NCNN model scale for dir '{modelDir}': {e.Message}");
				return 4;
            }
		}
	}
}
