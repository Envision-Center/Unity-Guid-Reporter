using System;
using System.Collections.Generic;
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
		private const string DEBUG_LOG_HEADER = "<color=#aaaaff>[Guid Reporter]</color>";

		public string path = "";
		public string searchPath = "";
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
		private void OnEnable()
		{
			UpdateLabels();
		}

		private void UpdateLabels()
		{
			pathFull = Path.Combine("Assets/", path);
			searchFull = Path.GetFullPath(Path.Combine(Application.dataPath, searchPath));
		}

		private void OnGUI()
		{
			GUILayout.BeginVertical();
			//Finding GUIDs
			GUILayout.Label("Enter a path underneath the Assets folder.");

			EditorGUI.BeginChangeCheck();
			path = EditorGUILayout.TextField("Asset Folder:", path);
			if (EditorGUI.EndChangeCheck())
			{
				UpdateLabels();
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
				UpdateLabels();
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

			float percent = (float)completion / (float)assetGuids.Count;
			GUILayout.Label($"{writeStatus}");
			GUILayout.Label("");
			EditorGUI.ProgressBar(GUILayoutUtility.GetLastRect(), percent, $"{referenceTask.Status.ToString()}... {completion}/{assetGuids.Count}");
			GUILayout.EndVertical();
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
			IEnumerable<string> filesInPath = FindFilesInPath(searchFull, GetExtensionsToSearch(), pathFull);
			Debug.Log($"{DEBUG_LOG_HEADER} Searching for {assetGuids.Count} GUIDs across {filesInPath.Count()} files.");
			for (int i = 0; i < assetGuids.Count; i++)
			{
				string guid = assetGuids[i];
				tasks.Add(SearchGuid(filesInPath, guid, AssetDatabase.GUIDToAssetPath(guid)));
			}
			
			await foreach (var t in UniTask.WhenEach(tasks))
			{
				if (cts.IsCancellationRequested)
				{
					throw new OperationCanceledException();
				}
				report += t.Result;
			}

			string writePath = Path.Combine(Directory.GetParent(Application.dataPath).ToString(), "report.txt");
			try
			{
				File.WriteAllText(writePath, report);
				writeStatus = $"Report saved to {writePath}";
				if (openFile)
				{
					System.Diagnostics.Process.Start(writePath);
				}
			}
			catch (Exception e)
			{
				writeStatus = "Failed to save report.";
			}
		}

		private async UniTask<string> SearchGuid(IEnumerable<string> files, string guid, string path)
		{
			await UniTask.SwitchToThreadPool();
			string file_report = "";
			List<string> results = new List<string>();

			results.AddRange(FindFilesContainingGuid(files, guid));

			if (results.Count > 0)
			{
				file_report += $"guid: {guid} | path: {path}" + Environment.NewLine;
				file_report += $"  References found: {results.Count}" + Environment.NewLine;
			}
			foreach (string file in results)
			{
				file_report += $"    {file}" + Environment.NewLine;
			}
			if (cts.IsCancellationRequested)
			{
				throw new OperationCanceledException();
			}
			completion++;
			await UniTask.SwitchToMainThread();
			Repaint();
			return file_report;
		}

		private static IEnumerable<string> FindFilesInPath(string folderPath, string[] extensions, string exclude)
		{
			try
			{
				Debug.Log($"{DEBUG_LOG_HEADER} Indexing files at {folderPath}");
				if (!Directory.Exists(folderPath))
					throw new DirectoryNotFoundException($"The folder path '{folderPath}' does not exist.");
				return Directory
					.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
					.Where(file => extensions.Any(file.ToLower().EndsWith))
					.Where(file => NotInPath(file, exclude));
			}
			catch(Exception e)
			{
				Debug.Log($"{DEBUG_LOG_HEADER}{e.Message}");
				return new List<string>();
			}
		}

		private static IEnumerable<string> FindFilesContainingGuid(IEnumerable<string> files, string guid)
		{
			return files
				.Where(file => File.ReadAllText(file).Contains(guid));
		}

		private static bool NotInPath(string file, string exclude)
		{
			string fileDirectory = Path.GetFullPath(Path.GetDirectoryName(file));
			string excludeDirectory = Path.GetFullPath(exclude);
			return !fileDirectory.Contains(excludeDirectory);
		}

		private bool IsBusySearching()
		{
			return referenceTask.Status == UniTaskStatus.Pending;
		}

		private string[] GetExtensionsToSearch()
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
			return extensions.ToArray();
		}
	}
}
