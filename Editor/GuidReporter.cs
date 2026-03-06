using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CollabXR.Objects;
using Cysharp.Threading.Tasks;
using openDicom.Registry;
using UnityEditor;
using UnityEngine;

namespace CollabXR
{
	public class GuidReporter : EditorWindow
	{
		public string path;
		public string searchPath;
		public string report;

		private string pathFull;
		private string searchFull;
		private string writeStatus;

		private bool openFile = true;

		private string[] filterOptions = new string[] { ".meta", ".prefab", ".asset", ".controller", ".anim" };
		private int filter = ~0;

		private List<string> assetGuids = new List<string>();

		UniTask referenceTask;

		CancellationTokenSource cts;

		int completion = 0;

		private void OnGUI()
		{
			//Finding GUIDs
			GUILayout.Label("Enter a path underneath the Assets folder.");

			EditorGUI.BeginChangeCheck();
			path = EditorGUILayout.TextField("Asset Folder:", path);
			if (EditorGUI.EndChangeCheck())
			{
				pathFull = Path.Combine("Assets/", path);
			}
			GUILayout.Label(pathFull);

			GUI.enabled = !IsBusySearching();
			if (GUILayout.Button("Grab Guids"))
			{
				GrabGuids();
			}
			GUI.enabled = true;

			GUILayout.Label($"{assetGuids.Count} guids found.");

			// Finding references

			EditorGUI.BeginChangeCheck();
			searchPath = EditorGUILayout.TextField("Folder to Search:", searchPath);
			if (EditorGUI.EndChangeCheck())
			{
				searchFull = Path.GetFullPath(Path.Combine(Application.dataPath, searchPath));
			}
			GUILayout.Label(searchFull);

			filter = EditorGUILayout.MaskField("File Extensions", filter, filterOptions);

			GUI.enabled = !IsBusySearching();
			if (GUILayout.Button("Find References"))
			{
				referenceTask = FindReferences();
			}

			GUI.enabled = IsBusySearching();
			if (GUILayout.Button("Cancel Operation"))
			{
				cts.Cancel();
			}
			GUI.enabled = true;

			openFile = GUILayout.Toggle(openFile, "Open report when completed");

			GUILayout.Label($"{referenceTask.Status.ToString()}... {completion}/{assetGuids.Count}");
			GUILayout.Label($"{writeStatus}");

			//scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(1000));
			//report = EditorGUILayout.TextArea(report);
			//EditorGUILayout.EndScrollView();
		}

		[MenuItem("Envision/Guid Reporter")]
		public static void ShowWindow()
		{
			GetWindow(typeof(GuidReporter), false, "Guid Reporter");
		}

		private void GrabGuids()
		{
			completion = 0;
			assetGuids = AssetDatabase.FindAssets("", new string[] { pathFull }).ToList();
		}

		private async UniTask FindReferences()
		{
			report = $"{assetGuids.Count} assets found in {pathFull}." + Environment.NewLine;
			report += $"Searching for references in files in {Path.GetFullPath(searchFull)}." + Environment.NewLine;
			report += "Including extensions: ";
			foreach (string extension in GetExtensionsToSearch())
			{
				report += $"{extension} ";
			}
			report += Environment.NewLine;
			cts = new CancellationTokenSource();
			completion = 0;
			List<UniTask<string>> tasks = new List<UniTask<string>>();
			for (int i = 0; i < assetGuids.Count; i++)
			{
				string guid = assetGuids[i];
				if (cts.IsCancellationRequested)
				{
					throw new OperationCanceledException();
				}
				tasks.Add(SearchGuid(guid, AssetDatabase.GUIDToAssetPath(guid)));
			}
			foreach (UniTask<string> task in tasks)
			{
				report += await task;
			}

			string writePath = Path.Combine(Directory.GetParent(Application.dataPath).ToString(), "report.txt");
			try
			{
				File.WriteAllText(writePath, report);
				writeStatus = $"Report saved to {writePath}";
				if (openFile)
				{
					Process.Start(writePath);
				}
			}
			catch (Exception e)
			{
				writeStatus = "Failed to save report.";
			}
		}

		private async UniTask<string> SearchGuid(string guid, string path)
		{
			await UniTask.SwitchToThreadPool();
			string file_report = "";
			List<string> results = new List<string>();

			foreach (string extension in GetExtensionsToSearch())
			{
				results.AddRange(FindFilesContainingGuid(searchFull, guid, extension, pathFull));
			}

			if (results.Count > 0)
			{
				file_report += $"guid: {guid} | path: {path}" + Environment.NewLine;
				file_report += $"  References found: {results.Count}" + Environment.NewLine;
			}
			foreach (string file in results)
			{
				file_report += $"    {file}" + Environment.NewLine;
			}
			completion++;
			await UniTask.SwitchToMainThread();
			Repaint();
			return file_report;
		}

		private static string[] FindFilesContainingGuid(string folderPath, string guid, string extension, string exclude)
		{
			if (!Directory.Exists(folderPath))
				throw new DirectoryNotFoundException($"The folder path '{folderPath}' does not exist.");

			return Directory
				.EnumerateFiles(folderPath, "*" + extension, SearchOption.AllDirectories)
				.Where(file => ShouldInclude(file, exclude))
				.Where(file => File.ReadAllText(file).Contains(guid))
				.ToArray();
		}

		private static bool ShouldInclude(string file, string exclude)
		{
			string fileDirectory = Path.GetFullPath(Path.GetDirectoryName(file));
			string excludeDirectory = Path.GetFullPath(exclude);
			return !fileDirectory.Contains(excludeDirectory);
		}

		private bool IsBusySearching()
		{
			return referenceTask.Status == UniTaskStatus.Pending;
		}

		private List<string> GetExtensionsToSearch()
		{
			List<string> extensions = new List<string>();
			for (int i = 0; i < filterOptions.Length; ++i)
			{
				bool enabled = (filter & (1 << i)) != 0;
				if (enabled)
				{
					extensions.Add(filterOptions[i]);
				}
			}
			return extensions;
		}
	}
}
