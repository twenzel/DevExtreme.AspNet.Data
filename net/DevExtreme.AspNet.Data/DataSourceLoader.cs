﻿using DevExtreme.AspNet.Data.Aggregation;
using DevExtreme.AspNet.Data.Helpers;
using DevExtreme.AspNet.Data.RemoteGrouping;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DevExtreme.AspNet.Data {

    public class DataSourceLoader {

        public static object Load<T>(IEnumerable<T> source, DataSourceLoadOptionsBase options) {
            var queryableSource = source.AsQueryable();
            var isLinqToObjects = queryableSource is EnumerableQuery;
            var builder = new DataSourceExpressionBuilder<T>(options, isLinqToObjects);

            if(options.IsCountQuery)
                return builder.BuildCountExpr().Compile()(queryableSource);

            if(!options.HasSort && !options.HasGroups && (options.Skip > 0 || options.Take > 0) && Compat.IsEntityFrramework(queryableSource.Provider)) {
                if(!options.HasDefaultSort)
                    options.DefaultSort = EFSorting.FindSortableMember(typeof(T));
            }

            var accessor = new DefaultAccessor<T>();
            var result = new DataSourceLoadResult();
            var emptyGroups = options.HasGroups && !options.Group.Last().GetIsExpanded();
            var canUseRemoteGrouping = options.RemoteGrouping.HasValue ? options.RemoteGrouping.Value : !isLinqToObjects;

            if(canUseRemoteGrouping && emptyGroups) {
                var groupingResult = ExecRemoteGrouping(queryableSource, builder, options);

                EmptyGroups(groupingResult.Groups, options.Group.Length);

                result.data = Paginate(groupingResult.Groups, options.Skip, options.Take);
                result.summary = groupingResult.Totals;
                result.totalCount = groupingResult.TotalCount;
            } else {
                var deferPaging = options.HasGroups || options.HasSummary && !canUseRemoteGrouping;
                var q = builder.BuildLoadExpr(!deferPaging).Compile();

                IEnumerable data = null;

                if(options.HasGroups)
                    data = new GroupHelper<T>(accessor).Group(q(queryableSource), options.Group);
                else
                    data = q(queryableSource).ToArray();

                // at this point, query is executed and data is in memory

                if(canUseRemoteGrouping && options.HasSummary && !options.HasGroups) {
                    var groupingResult = ExecRemoteGrouping(queryableSource, builder, options);
                    result.totalCount = groupingResult.TotalCount;
                    result.summary = groupingResult.Totals;
                } else {
                    if(options.RequireTotalCount)
                        result.totalCount = builder.BuildCountExpr().Compile()(queryableSource);

                    if(options.HasSummary)
                        result.summary = new AggregateCalculator<T>(data, accessor, options.TotalSummary, options.GroupSummary).Run();
                }

                if(deferPaging)
                    data = Paginate(data, options.Skip, options.Take);

                if(emptyGroups)
                    EmptyGroups(data, options.Group.Length);

                result.data = data;
            }

            if(result.IsDataOnly())
                return result.data;

            return result;
        }

        static RemoteGroupingResult ExecRemoteGrouping<T>(IQueryable<T> source, DataSourceExpressionBuilder<T> builder, DataSourceLoadOptionsBase options) {
            return RemoteGroupTransformer.Run(
                builder.BuildLoadGroupsExpr().Compile()(source).ToArray(),
                options.HasGroups ? options.Group.Length : 0,
                options.TotalSummary,
                options.GroupSummary
            );
        }

        static IEnumerable Paginate(IEnumerable data, int skip, int take) {
            if(skip < 1 && take < 1)
                return data;

            var typed = data.Cast<object>();

            if(skip > 0)
                typed = typed.Skip(skip);

            if(take > 0)
                typed = typed.Take(take);

            return typed;
        }

        static void EmptyGroups(IEnumerable groups, int level) {
            foreach(Group g in groups) {
                if(level < 2) {
                    var remoteGroup = g.items[0] as IRemoteGroup;

                    if(remoteGroup != null) {
                        g.count = remoteGroup.Count;
                    } else {
                        g.count = g.items.Count;
                    }

                    g.items = null;
                } else {
                    EmptyGroups(g.items, level - 1);
                }
            }
        }
    }

}
