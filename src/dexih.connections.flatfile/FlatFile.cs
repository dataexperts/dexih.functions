﻿using dexih.functions;

namespace dexih.connections.flatfile
{
	public class FlatFile : Table
	{
		private string _fileRootPath;
		private string _fileIncomingPath = "incoming";
		private string _fileProcessedPath = "processed";
		private string _fileRejectedPath = "rejected";
		private string _fileMatchPattern;

		public bool UseCustomFilePaths { get; set; }

		public string FileRootPath {
			get => UseCustomFilePaths ? Name : _fileRootPath;
			set => _fileRootPath = value;
		}

		public string FileIncomingPath
		{
			get => UseCustomFilePaths ? "Incoming" : _fileIncomingPath;
			set => _fileIncomingPath = value;
		}

		public string FileProcessedPath
		{
			get => UseCustomFilePaths ? "Processed" : _fileProcessedPath;
			set => _fileProcessedPath = value;
		}

		public string FileRejectedPath
		{
			get => UseCustomFilePaths ? "Rejected" : _fileRejectedPath;
			set => _fileRejectedPath = value;
		}

		public string FileMatchPattern
		{
			get => UseCustomFilePaths ? "*" : _fileMatchPattern;
			set => _fileMatchPattern = value;
		}

		public FileFormat FileFormat { get; set; } = new FileFormat();
		public string FileSample { get; set; }

		public FlatFile()
		{
		}
	}
}
