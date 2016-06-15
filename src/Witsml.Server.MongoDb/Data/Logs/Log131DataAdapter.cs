﻿//----------------------------------------------------------------------- 
// PDS.Witsml.Server, 2016.1
//
// Copyright 2016 Petrotechnical Data Systems
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//   
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Energistics.DataAccess.WITSML131;
using Energistics.DataAccess.WITSML131.ComponentSchemas;
using Energistics.DataAccess.WITSML131.ReferenceData;
using Energistics.Datatypes;
using Energistics.Datatypes.ChannelData;
using MongoDB.Driver;
using PDS.Framework;
using PDS.Witsml.Data.Channels;
using PDS.Witsml.Data.Logs;
using PDS.Witsml.Server.Configuration;
using PDS.Witsml.Server.Data.Channels;

namespace PDS.Witsml.Server.Data.Logs
{
    /// <summary>
    /// Data adapter that encapsulates CRUD functionality for a 131 <see cref="Log" />
    /// </summary>
    /// <seealso cref="PDS.Witsml.Server.Data.Logs.LogDataAdapter{Log, LogCurveInfo}" />
    [Export(typeof(IWitsmlDataAdapter<Log>))]
    [Export(typeof(IWitsml131Configuration))]
    [Export131(ObjectTypes.Log, typeof(IChannelDataProvider))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class Log131DataAdapter : LogDataAdapter<Log, LogCurveInfo>, IWitsml131Configuration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Log131DataAdapter"/> class.
        /// </summary>
        /// <param name="databaseProvider">The database provider.</param>
        [ImportingConstructor]
        public Log131DataAdapter(IDatabaseProvider databaseProvider) : base(databaseProvider, ObjectNames.Log131)
        {
        }

        /// <summary>
        /// Gets the supported capabilities for the <see cref="Log"/> object.
        /// </summary>
        /// <param name="capServer">The capServer instance.</param>
        public void GetCapabilities(CapServer capServer)
        {
            Logger.DebugFormat("Getting the supported capabilities for Log data version {0}.", capServer.Version);

            capServer.Add(Functions.GetFromStore, ObjectTypes.Log);
            capServer.Add(Functions.AddToStore, ObjectTypes.Log);
            capServer.Add(Functions.UpdateInStore, ObjectTypes.Log);
            capServer.Add(Functions.DeleteFromStore, ObjectTypes.Log);
        }

        /// <summary>
        /// Adds a <see cref="Log" /> entity to the data store.
        /// </summary>
        /// <param name="parser">The input template parser.</param>
        /// <param name="dataObject">The Log instance to add to the store.</param>
        public override void Add(WitsmlQueryParser parser, Log dataObject)
        {
            using (var transaction = DatabaseProvider.BeginTransaction())
            {
                ClearIndexValues(dataObject);

                // Extract Data
                var reader = ExtractDataReader(dataObject);
                InsertEntity(dataObject, transaction);
                UpdateLogDataAndIndexRange(dataObject.GetUri(), new[] { reader }, transaction);
                transaction.Commit();
            }               
        }

        /// <summary>
        /// Updates the specified <see cref="Log" /> instance in the store.
        /// </summary>
        /// <param name="parser">The update parser.</param>
        /// <param name="dataObject">The data object to be updated.</param>
        public override void Update(WitsmlQueryParser parser, Log dataObject)
        {
            var uri = dataObject.GetUri();
            using (var transaction = DatabaseProvider.BeginTransaction(uri))
            {
                UpdateEntity(parser, uri, transaction);

                // Update Log Data and Index Range
                var reader = ExtractDataReader(dataObject, GetEntity(uri));
                UpdateLogDataAndIndexRange(uri, new[] { reader }, transaction);
                transaction.Commit();
            }
        }

        /// <summary>
        /// Creates a generic measure.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="uom">The uom.</param>
        /// <returns></returns>
        protected override object CreateGenericMeasure(double value, string uom)
        {
            return new GenericMeasure() { Value = value, Uom = uom };
        }

        /// <summary>
        /// Determines whether the specified log is increasing.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <returns></returns>
        protected override bool IsIncreasing(Log log)
        {
            return log.IsIncreasing();
        }

        /// <summary>
        /// Determines whether the specified log is a time log.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="includeElapsedTime">if set to <c>true</c>, include elapsed time.</param>
        /// <returns></returns>
        protected override bool IsTimeLog(Log log, bool includeElapsedTime = false)
        {
            return log.IsTimeLog(includeElapsedTime);
        }

        /// <summary>
        /// Gets the log curve.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="mnemonic">The mnemonic.</param>
        /// <returns></returns>
        protected override LogCurveInfo GetLogCurve(Log log, string mnemonic)
        {
            return log?.LogCurveInfo.GetByUid(mnemonic) ?? log?.LogCurveInfo.GetByMnemonic(mnemonic);
        }

        /// <summary>
        /// Gets the log curves.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <returns></returns>
        protected override List<LogCurveInfo> GetLogCurves(Log log)
        {
            return log.LogCurveInfo;
        }

        /// <summary>
        /// Gets the mnemonic.
        /// </summary>
        /// <param name="curve">The curve.</param>
        /// <returns></returns>
        protected override string GetMnemonic(LogCurveInfo curve)
        {
            return curve?.Mnemonic;
        }

        /// <summary>
        /// Gets the index curve mnemonic.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <returns></returns>
        protected override string GetIndexCurveMnemonic(Log log)
        {
            return log.IndexCurve.Value;
        }

        /// <summary>
        /// Gets the units by column index.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <returns></returns>
        protected override IDictionary<int, string> GetUnitsByColumnIndex(Log log)
        {
            return log.LogCurveInfo
                .Select(x => x.Unit)
                .ToArray()
                .Select((unit, index) => new { Unit = unit, Index = index })
                .ToDictionary(x => x.Index, x => x.Unit);
        }

        /// <summary>
        /// Gets the null values by column index.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <returns></returns>
        protected override IDictionary<int, string> GetNullValuesByColumnIndex(Log log)
        {
            return log.LogCurveInfo
                .Select(x => x.NullValue)
                .ToArray()
                .Select((nullValue, index) => new { NullValue = string.IsNullOrWhiteSpace(nullValue) ? log.NullValue : nullValue, Index = index })
                .ToDictionary(x => x.Index, x => x.NullValue);
        }

        /// <summary>
        /// Gets the index range.
        /// </summary>
        /// <param name="curve">The curve.</param>
        /// <param name="increasing">if set to <c>true</c> [increasing].</param>
        /// <param name="isTimeIndex">if set to <c>true</c> [is time index].</param>
        /// <returns></returns>
        protected override Range<double?> GetIndexRange(LogCurveInfo curve, bool increasing = true, bool isTimeIndex = false)
        {
            return curve.GetIndexRange(increasing, isTimeIndex);
        }

        /// <summary>
        /// Sets the log data values.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="logDataValues">The log data values.</param>
        /// <param name="mnemonics">The mnemonics.</param>
        /// <param name="units">The units.</param>
        protected override void SetLogDataValues(Log log, List<string> logDataValues, IEnumerable<string> mnemonics, IEnumerable<string> units)
        {
            log.LogData = logDataValues;
        }

        /// <summary>
        /// Sets the log index range.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="logHeader">The log that has the header information.</param>
        /// <param name="ranges">The ranges.</param>
        /// <param name="indexCurve">The index curve.</param>
        protected override void SetLogIndexRange(Log log, Log logHeader, Dictionary<string, Range<double?>> ranges, string indexCurve)
        {
            var isTimeLog = IsTimeLog(logHeader);

            if (log.LogCurveInfo != null)
            {
                foreach (var logCurve in log.LogCurveInfo)
                {
                    var mnemonic = logCurve.Mnemonic;
                    Range<double?> range;

                    if (!ranges.TryGetValue(mnemonic, out range))
                        continue;

                    // Sort range in min/max order
                    range = range.Sort();

                    if (isTimeLog)
                    {
                        if (range.Start.HasValue && !double.IsNaN(range.Start.Value))
                            logCurve.MinDateTimeIndex = DateTimeExtensions.FromUnixTimeMicroseconds((long)range.Start.Value).DateTime;
                        if (range.End.HasValue && !double.IsNaN(range.End.Value))
                            logCurve.MaxDateTimeIndex = DateTimeExtensions.FromUnixTimeMicroseconds((long)range.End.Value).DateTime;
                    }
                    else
                    {
                        if (range.Start.HasValue)
                            logCurve.MinIndex.Value = range.Start.Value;
                        if (range.End.HasValue)
                            logCurve.MaxIndex.Value = range.End.Value;
                    }
                }
            }

            // Set index curve range separately, since logCurveInfo may not exist
            SetIndexCurveRange(log, ranges, indexCurve, isTimeLog);
        }

        /// <summary>
        /// Updates the common data.
        /// </summary>
        /// <param name="logHeaderUpdate">The log header update.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="offset">The offset.</param>
        /// <returns></returns>
        protected override UpdateDefinition<Log> UpdateCommonData(UpdateDefinition<Log> logHeaderUpdate, Log entity, TimeSpan? offset)
        {
            if (entity?.CommonData == null)
                return logHeaderUpdate;

            if (entity.CommonData.DateTimeCreation.HasValue)
            {
                var creationTime = entity.CommonData.DateTimeCreation; //.ToOffsetTime(offset);
                logHeaderUpdate = MongoDbUtility.BuildUpdate(logHeaderUpdate, "CommonData.DateTimeCreation", creationTime?.ToString("o"));
                Logger.DebugFormat("Updating Common Data create time to '{0}'", creationTime);
            }

            if (entity.CommonData.DateTimeLastChange.HasValue)
            {
                var updateTime = entity.CommonData.DateTimeLastChange; //.ToOffsetTime(offset);
                logHeaderUpdate = MongoDbUtility.BuildUpdate(logHeaderUpdate, "CommonData.DateTimeLastChange", updateTime?.ToString("o"));
                Logger.DebugFormat("Updating Common Data update time to '{0}'", updateTime);
            }

            return logHeaderUpdate;
        }

        /// <summary>
        /// Converts a logCurveInfo to an index metadata record.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="indexCurve">The index curve.</param>
        /// <param name="scale">The scale.</param>
        /// <returns></returns>
        protected override IndexMetadataRecord ToIndexMetadataRecord(Log entity, LogCurveInfo indexCurve, int scale = 3)
        {
            return new IndexMetadataRecord()
            {
                Uri = indexCurve.GetUri(entity),
                Mnemonic = indexCurve.Mnemonic,
                Description = indexCurve.CurveDescription,
                Uom = indexCurve.Unit,
                Scale = scale,
                IndexType = IsTimeLog(entity, true)
                    ? ChannelIndexTypes.Time
                    : ChannelIndexTypes.Depth,
                Direction = IsIncreasing(entity)
                    ? IndexDirections.Increasing
                    : IndexDirections.Decreasing,
                CustomData = new Dictionary<string, DataValue>(0),
            };
        }

        /// <summary>
        /// Converts a logCurveInfo to a channel metadata record.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="curve">The curve.</param>
        /// <param name="indexMetadata">The index metadata.</param>
        /// <returns></returns>
        protected override ChannelMetadataRecord ToChannelMetadataRecord(Log entity, LogCurveInfo curve, IndexMetadataRecord indexMetadata)
        {
            var uri = curve.GetUri(entity);
            var isTimeLog = IsTimeLog(entity, true);
            var curveIndexes = GetCurrentIndexRange(entity);

            return new ChannelMetadataRecord()
            {
                ChannelUri = uri,
                ContentType = uri.ContentType,
                DataType = curve.TypeLogData.GetValueOrDefault(LogDataType.@double).ToString().Replace("@", string.Empty),
                Description = curve.CurveDescription ?? curve.Mnemonic,
                Mnemonic = curve.Mnemonic,
                Uom = curve.Unit,
                MeasureClass = curve.ClassWitsml?.Name ?? ObjectTypes.Unknown,
                Source = curve.DataSource ?? ObjectTypes.Unknown,
                Uuid = curve.Mnemonic,
                Status = ChannelStatuses.Active,
                ChannelAxes = new List<ChannelAxis>(),
                StartIndex = curveIndexes[curve.Mnemonic].Start.IndexToScale(indexMetadata.Scale, isTimeLog),
                EndIndex = curveIndexes[curve.Mnemonic].End.IndexToScale(indexMetadata.Scale, isTimeLog),
                Indexes = new List<IndexMetadataRecord>()
                {
                    indexMetadata
                }
            };
        }

        /// <summary>
        /// Extracts the data reader.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="existing">The existing.</param>
        /// <returns></returns>
        private ChannelDataReader ExtractDataReader(Log entity, Log existing = null)
        {
            if (existing == null)
            {
                var reader = entity.GetReader();
                entity.LogData = null;
                return reader;
            }

            existing.LogData = entity.LogData;
            return existing.GetReader();
        }

        /// <summary>
        /// Clears the index values.
        /// </summary>
        /// <param name="dataObject">The data object.</param>
        private void ClearIndexValues(Log dataObject)
        {
            if (IsTimeLog(dataObject))
            {
                dataObject.StartDateTimeIndex = null;
                dataObject.StartDateTimeIndexSpecified = false;
                dataObject.EndDateTimeIndex = null;
                dataObject.EndDateTimeIndexSpecified = false;

                foreach (var curve in dataObject.LogCurveInfo)
                {
                    curve.MinDateTimeIndex = null;
                    curve.MinDateTimeIndexSpecified = false;
                    curve.MaxDateTimeIndex = null;
                    curve.MaxDateTimeIndexSpecified = false;
                }
            }
            else
            {
                dataObject.StartIndex = null;
                dataObject.EndIndex = null;

                foreach (var curve in dataObject.LogCurveInfo)
                {
                    curve.MinIndex = null;
                    curve.MaxIndex = null;
                }
            }
        }

        private void SetIndexCurveRange(Log log, Dictionary<string, Range<double?>> ranges, string indexCurve, bool isTimeLog)
        {
            if (!ranges.ContainsKey(indexCurve))
                return;

            var range = ranges[indexCurve];
            if (isTimeLog)
            {
                if (log.StartDateTimeIndex != null)
                {
                    if (range.Start.HasValue && !double.IsNaN(range.Start.Value))
                        log.StartDateTimeIndex =
                            DateTimeExtensions.FromUnixTimeMicroseconds((long)range.Start.Value).DateTime;
                    else
                        log.StartDateTimeIndex = null;
                }

                if (log.EndDateTimeIndex != null)
                {
                    if (range.End.HasValue && !double.IsNaN(range.End.Value))
                        log.EndDateTimeIndex =
                            DateTimeExtensions.FromUnixTimeMicroseconds((long)range.End.Value).DateTime;
                    else
                        log.EndDateTimeIndex = null;
                }
            }
            else
            {
                if (log.StartIndex != null)
                {
                    if (range.Start.HasValue)
                        log.StartIndex.Value = range.Start.Value;
                    else
                        log.StartIndex = null;
                }

                if (log.EndIndex != null)
                {
                    if (range.End.HasValue)
                        log.EndIndex.Value = range.End.Value;
                    else
                        log.EndIndex = null;
                }
            }
        }
    }
}
