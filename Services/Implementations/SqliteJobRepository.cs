using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public sealed class SqliteJobRepository : IJobRepository
    {
        private readonly string _connectionString;
        private readonly ILoggingService _log;

        public SqliteJobRepository(string dbPath, ILoggingService log)
        {
            _log = log;
            var dir = Path.GetDirectoryName(dbPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Jobs (
                    JobId TEXT PRIMARY KEY,
                    InputFolder TEXT NOT NULL,
                    OutputFolder TEXT NOT NULL,
                    Steps INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    Progress REAL NOT NULL,
                    FilesProcessed INTEGER NOT NULL,
                    TotalFiles INTEGER NOT NULL,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NULL,
                    OcrQuality INTEGER NOT NULL,
                    OcrLanguage TEXT NOT NULL,
                    PipelineJson TEXT NULL,
                    TriggerType INTEGER NOT NULL DEFAULT 0
                );
                
                CREATE TABLE IF NOT EXISTS JobFileStatus (
                    JobId TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    Status INTEGER NOT NULL, -- 1=Success, 2=Failed
                    ErrorMessage TEXT NULL,
                    Timestamp TEXT NOT NULL,
                    FileHash TEXT NULL,
                    PRIMARY KEY (JobId, FilePath)
                );";
            cmd.ExecuteNonQuery();

            // Migration: Add PipelineJson if missing
            try
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = "ALTER TABLE Jobs ADD COLUMN PipelineJson TEXT NULL;";
                cmdAlter.ExecuteNonQuery();
            }
            catch { /* Ignore if exists */ }

            // Migration: Add TriggerType if missing
            try
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = "ALTER TABLE Jobs ADD COLUMN TriggerType INTEGER NOT NULL DEFAULT 0;";
                cmdAlter.ExecuteNonQuery();
            }
            catch { /* Ignore if exists */ }

            // Migration: Add FileHash if missing
            try
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = "ALTER TABLE JobFileStatus ADD COLUMN FileHash TEXT NULL;";
                cmdAlter.ExecuteNonQuery();
            }
            catch { /* Ignore if exists */ }
        }

        public void AddJob(ProcessingJob job)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Jobs (
                    JobId, InputFolder, OutputFolder, Steps, Status, Progress,
                    FilesProcessed, TotalFiles, StartTime, EndTime, OcrQuality, OcrLanguage, PipelineJson, TriggerType
                ) VALUES (
                    @JobId, @InputFolder, @OutputFolder, @Steps, @Status, @Progress,
                    @FilesProcessed, @TotalFiles, @StartTime, @EndTime, @OcrQuality, @OcrLanguage, @PipelineJson, @TriggerType
                );";

            BindAll(cmd, job);
            cmd.ExecuteNonQuery();
        }

        public void UpdateJob(ProcessingJob job)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Jobs SET
                    InputFolder = @InputFolder,
                    OutputFolder = @OutputFolder,
                    Steps = @Steps,
                    Status = @Status,
                    Progress = @Progress,
                    FilesProcessed = @FilesProcessed,
                    TotalFiles = @TotalFiles,
                    StartTime = @StartTime,
                    EndTime = @EndTime,
                    OcrQuality = @OcrQuality,
                    OcrLanguage = @OcrLanguage,
                    PipelineJson = @PipelineJson,
                    TriggerType = @TriggerType
                WHERE JobId = @JobId;";

            BindAll(cmd, job);
            cmd.ExecuteNonQuery();
        }

        public ProcessingJob? GetJob(Guid jobId)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Jobs WHERE JobId = @JobId";
            cmd.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var job = Map(reader);

            // Load file statuses
            reader.Close();

            using var cmdFiles = conn.CreateCommand();
            cmdFiles.CommandText = "SELECT FilePath, Status, ErrorMessage, Timestamp FROM JobFileStatus WHERE JobId = @JobId";
            cmdFiles.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });

            using var fileReader = cmdFiles.ExecuteReader();
            while (fileReader.Read())
            {
                var path = fileReader.GetString(0);
                var status = fileReader.GetInt32(1);
                var error = fileReader.IsDBNull(2) ? null : fileReader.GetString(2);
                var ts = DateTime.Parse(fileReader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                if (status == 1) // Success
                {
                    job.ProcessedFiles.Add(path);
                }
                else if (status == 2) // Failed
                {
                    job.Errors.Add(new FileProcessingError
                    {
                        FileName = Path.GetFileName(path),
                        FilePath = path,
                        ErrorMessage = error ?? "Unknown error",
                        Timestamp = ts
                    });
                }
            }

            return job;
        }

        public void RecordFileSuccess(Guid jobId, string filePath, string? fileHash = null)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO JobFileStatus (JobId, FilePath, Status, ErrorMessage, Timestamp, FileHash)
                VALUES (@JobId, @FilePath, 1, NULL, @Timestamp, @FileHash);";

            cmd.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });
            cmd.Parameters.Add(new SqliteParameter("@FilePath", SqliteType.Text) { Value = filePath });
            cmd.Parameters.Add(new SqliteParameter("@Timestamp", SqliteType.Text) { Value = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) });
            cmd.Parameters.Add(new SqliteParameter("@FileHash", SqliteType.Text) { Value = (object?)fileHash ?? DBNull.Value });

            cmd.ExecuteNonQuery();
        }

        public void RecordFileFailure(Guid jobId, string filePath, string error)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO JobFileStatus (JobId, FilePath, Status, ErrorMessage, Timestamp, FileHash)
                VALUES (@JobId, @FilePath, 2, @ErrorMessage, @Timestamp, NULL);";

            cmd.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });
            cmd.Parameters.Add(new SqliteParameter("@FilePath", SqliteType.Text) { Value = filePath });
            cmd.Parameters.Add(new SqliteParameter("@ErrorMessage", SqliteType.Text) { Value = error });
            cmd.Parameters.Add(new SqliteParameter("@Timestamp", SqliteType.Text) { Value = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) });

            cmd.ExecuteNonQuery();
        }

        public void ClearJobHistory(Guid jobId)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM JobFileStatus WHERE JobId = @JobId";
            cmd.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });
            cmd.ExecuteNonQuery();
        }

        public string? GetFileHash(Guid jobId, string filePath)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT FileHash FROM JobFileStatus WHERE JobId = @JobId AND FilePath = @FilePath AND Status = 1";
            cmd.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });
            cmd.Parameters.Add(new SqliteParameter("@FilePath", SqliteType.Text) { Value = filePath });
            
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : result.ToString();
        }


        public IEnumerable<ProcessingJob> GetAllJobs()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Jobs ORDER BY StartTime DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                yield return Map(reader);
        }

        private static void BindAll(SqliteCommand cmd, ProcessingJob job)
        {
            cmd.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = job.JobId.ToString() });
            cmd.Parameters.Add(new SqliteParameter("@InputFolder", SqliteType.Text) { Value = job.InputFolder });
            cmd.Parameters.Add(new SqliteParameter("@OutputFolder", SqliteType.Text) { Value = job.OutputFolder });
            cmd.Parameters.Add(new SqliteParameter("@Steps", SqliteType.Integer) { Value = (int)job.Steps });
            cmd.Parameters.Add(new SqliteParameter("@Status", SqliteType.Integer) { Value = (int)job.Status });
            cmd.Parameters.Add(new SqliteParameter("@Progress", SqliteType.Real) { Value = job.Progress });
            cmd.Parameters.Add(new SqliteParameter("@FilesProcessed", SqliteType.Integer) { Value = job.FilesProcessed });
            cmd.Parameters.Add(new SqliteParameter("@TotalFiles", SqliteType.Integer) { Value = job.TotalFiles });
            cmd.Parameters.Add(new SqliteParameter("@StartTime", SqliteType.Text) { Value = job.StartTime.ToString("o", CultureInfo.InvariantCulture) });

            cmd.Parameters.Add(new SqliteParameter("@EndTime", SqliteType.Text)
            {
                Value = job.EndTime.HasValue
                    ? job.EndTime.Value.ToString("o", CultureInfo.InvariantCulture)
                    : DBNull.Value
            });

            cmd.Parameters.Add(new SqliteParameter("@OcrQuality", SqliteType.Integer) { Value = (int)job.OcrQuality });
            cmd.Parameters.Add(new SqliteParameter("@OcrLanguage", SqliteType.Text) { Value = job.OcrLanguage });
            cmd.Parameters.Add(new SqliteParameter("@PipelineJson", SqliteType.Text) { Value = (object?)job.PipelineJson ?? DBNull.Value });
            cmd.Parameters.Add(new SqliteParameter("@TriggerType", SqliteType.Integer) { Value = (int)job.TriggerType });
        }

        private static ProcessingJob Map(SqliteDataReader r)
        {
            return new ProcessingJob
            {
                JobId = Guid.Parse(r.GetString(r.GetOrdinal("JobId"))),
                InputFolder = r.GetString(r.GetOrdinal("InputFolder")),
                OutputFolder = r.GetString(r.GetOrdinal("OutputFolder")),
                Steps = (ProcessingStep)r.GetInt32(r.GetOrdinal("Steps")),
                Status = (JobStatus)r.GetInt32(r.GetOrdinal("Status")),
                Progress = r.GetDouble(r.GetOrdinal("Progress")),
                FilesProcessed = r.GetInt32(r.GetOrdinal("FilesProcessed")),
                TotalFiles = r.GetInt32(r.GetOrdinal("TotalFiles")),
                StartTime = DateTime.Parse(r.GetString(r.GetOrdinal("StartTime")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                EndTime = r.IsDBNull(r.GetOrdinal("EndTime"))
                    ? null
                    : DateTime.Parse(r.GetString(r.GetOrdinal("EndTime")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                OcrQuality = (OcrQuality)r.GetInt32(r.GetOrdinal("OcrQuality")),
                OcrLanguage = r.GetString(r.GetOrdinal("OcrLanguage")),
                PipelineJson = r.IsDBNull(r.GetOrdinal("PipelineJson")) ? null : r.GetString(r.GetOrdinal("PipelineJson")),
                TriggerType = r.GetOrdinal("TriggerType") >= 0 ? (JobTriggerType)r.GetInt32(r.GetOrdinal("TriggerType")) : JobTriggerType.Manual
            };
        }
        public void DeleteJob(Guid jobId)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                using var cmdFiles = conn.CreateCommand();
                cmdFiles.Transaction = transaction;
                cmdFiles.CommandText = "DELETE FROM JobFileStatus WHERE JobId = @JobId";
                cmdFiles.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });
                cmdFiles.ExecuteNonQuery();

                using var cmdJob = conn.CreateCommand();
                cmdJob.Transaction = transaction;
                cmdJob.CommandText = "DELETE FROM Jobs WHERE JobId = @JobId";
                cmdJob.Parameters.Add(new SqliteParameter("@JobId", SqliteType.Text) { Value = jobId.ToString() });
                cmdJob.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
