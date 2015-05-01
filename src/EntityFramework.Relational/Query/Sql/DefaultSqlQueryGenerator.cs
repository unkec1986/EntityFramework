// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using JetBrains.Annotations;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors;
using Microsoft.Data.Entity.Utilities;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.Sql
{
    public class DefaultSqlQueryGenerator : ThrowingExpressionTreeVisitor, ISqlExpressionVisitor, ISqlQueryGenerator
    {
        private readonly SelectExpression _selectExpression;

        private IndentedStringBuilder _sql;
        private List<CommandParameter> _commandParameters;
        private IDictionary<string, object> _parameterValues;

        public DefaultSqlQueryGenerator([NotNull] SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            _selectExpression = selectExpression;
        }

        public virtual string GenerateSql(IDictionary<string, object> parameterValues)
        {
            Check.NotNull(parameterValues, nameof(parameterValues));

            _sql = new IndentedStringBuilder();
            _commandParameters = new List<CommandParameter>();
            _parameterValues = parameterValues;

            _selectExpression.Accept(this);

            return _sql.ToString();
        }

        public virtual IReadOnlyList<CommandParameter> Parameters => _commandParameters;

        protected virtual IndentedStringBuilder Sql => _sql;

        protected virtual string ConcatOperator => "+";
        protected virtual string ParameterPrefix => "@";
        protected virtual string TrueLiteral => "1";
        protected virtual string FalseLiteral => "0";
        protected virtual string TypedTrueLiteral => "CAST(1 AS BIT)";
        protected virtual string TypedFalseLiteral => "CAST(0 AS BIT)";

        public virtual Expression VisitSelectExpression(SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            IDisposable subQueryIndent = null;

            if (selectExpression.Alias != null)
            {
                _sql.AppendLine("(");

                subQueryIndent = _sql.Indent();
            }

            _sql.Append("SELECT ");

            if (selectExpression.IsDistinct)
            {
                _sql.Append("DISTINCT ");
            }

            GenerateTop(selectExpression);

            if (selectExpression.Projection.Any())
            {
                VisitJoin(selectExpression.Projection);
            }
            else if (selectExpression.ProjectionExpression != null)
            {
                VisitExpression(selectExpression.ProjectionExpression);
            }
            else if (selectExpression.IsProjectStar)
            {
                _sql.Append(DelimitIdentifier(selectExpression.Tables.Single().Alias))
                    .Append(".*");
            }
            else
            {
                _sql.Append("1");
            }

            if (selectExpression.Tables.Any())
            {
                _sql.AppendLine()
                    .Append("FROM ");

                VisitJoin(selectExpression.Tables, sql => sql.AppendLine());
            }

            if (selectExpression.Predicate != null)
            {
                _sql.AppendLine()
                    .Append("WHERE ");

                var constantExpression = selectExpression.Predicate as ConstantExpression;

                if (constantExpression != null)
                {
                    _sql.Append((bool)constantExpression.Value ? "1 = 1" : "1 = 0");
                }
                else
                {
                    var predicate = new NullComparisonTransformingVisitor(_parameterValues)
                        .VisitExpression(selectExpression.Predicate);

                    // we have to optimize out comparisons to null-valued parameters before we can expand null semantics 
                    if (_parameterValues.Count > 0)
                    {
                        var optimizedNullExpansionVisitor = new NullSemanticsOptimizedExpandingVisitor();
                        var nullSemanticsExpandedOptimized = optimizedNullExpansionVisitor.VisitExpression(predicate);
                        if (optimizedNullExpansionVisitor.OptimizedExpansionPossible)
                        {
                            predicate = nullSemanticsExpandedOptimized;
                        }
                        else
                        {
                            predicate = new NullSemanticsExpandingVisitor()
                                .VisitExpression(predicate);
                        }
                    }

                    predicate = new ReducingExpressionVisitor().VisitExpression(predicate);

                    VisitExpression(predicate);

                    if (selectExpression.Predicate is ParameterExpression
                        || selectExpression.Predicate.IsAliasWithColumnExpression())
                    {
                        _sql.Append(" = ");
                        _sql.Append(TrueLiteral);
                    }
                }
            }

            if (selectExpression.OrderBy.Any())
            {
                _sql.AppendLine()
                    .Append("ORDER BY ");

                VisitJoin(selectExpression.OrderBy, t =>
                    {
                        var aliasExpression = t.Expression as AliasExpression;
                        if (aliasExpression != null)
                        {
                            if (aliasExpression.Alias != null)
                            {
                                _sql.Append(DelimitIdentifier(aliasExpression.Alias));
                            }
                            else
                            {
                                VisitExpression(aliasExpression.Expression);
                            }
                        }
                        else
                        {
                            VisitExpression(t.Expression);
                        }

                        if (t.OrderingDirection == OrderingDirection.Desc)
                        {
                            _sql.Append(" DESC");
                        }
                    });
            }

            GenerateLimitOffset(selectExpression);

            if (subQueryIndent != null)
            {
                subQueryIndent.Dispose();

                _sql.AppendLine()
                    .Append(") AS ")
                    .Append(DelimitIdentifier(selectExpression.Alias));
            }

            return selectExpression;
        }

        private void VisitJoin(
            IReadOnlyList<Expression> expressions, Action<IndentedStringBuilder> joinAction = null)
            => VisitJoin(expressions, e => VisitExpression(e), joinAction);

        private void VisitJoin<T>(
            IReadOnlyList<T> items, Action<T> itemAction, Action<IndentedStringBuilder> joinAction = null)
        {
            joinAction = joinAction ?? (isb => isb.Append(", "));

            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    joinAction(_sql);
                }

                itemAction(items[i]);
            }
        }

        public virtual Expression VisitRawSqlDerivedTableExpression(RawSqlDerivedTableExpression rawSqlDerivedTableExpression)
        {
            Check.NotNull(rawSqlDerivedTableExpression, nameof(rawSqlDerivedTableExpression));

            _sql.AppendLine("(");

            using (_sql.Indent())
            {
                var substitutions = new object[rawSqlDerivedTableExpression.Parameters.Count()];

                for (var index = 0; index < rawSqlDerivedTableExpression.Parameters.Count(); index++)
                {
                    var parameterName = "p" + index;

                    _commandParameters.Add(new CommandParameter(parameterName, rawSqlDerivedTableExpression.Parameters[index]));

                    substitutions[index] = ParameterPrefix + parameterName;
                }

                _sql.AppendLines(string.Format(
                    rawSqlDerivedTableExpression.Sql,
                    substitutions));
            }

            _sql.Append(") AS ")
                .Append(DelimitIdentifier(rawSqlDerivedTableExpression.Alias));

            return rawSqlDerivedTableExpression;
        }

        public virtual Expression VisitTableExpression(TableExpression tableExpression)
        {
            Check.NotNull(tableExpression, nameof(tableExpression));

            if (tableExpression.Schema != null)
            {
                _sql.Append(DelimitIdentifier(tableExpression.Schema))
                    .Append(".");
            }

            _sql.Append(DelimitIdentifier(tableExpression.Table))
                .Append(" AS ")
                .Append(DelimitIdentifier(tableExpression.Alias));

            return tableExpression;
        }

        public virtual Expression VisitCrossJoinExpression(CrossJoinExpression crossJoinExpression)
        {
            Check.NotNull(crossJoinExpression, nameof(crossJoinExpression));

            _sql.Append("CROSS JOIN ");

            VisitExpression(crossJoinExpression.TableExpression);

            return crossJoinExpression;
        }

        public virtual Expression VisitCountExpression(CountExpression countExpression)
        {
            Check.NotNull(countExpression, nameof(countExpression));

            _sql.Append("COUNT(*)");

            return countExpression;
        }

        public virtual Expression VisitSumExpression(SumExpression sumExpression)
        {
            Check.NotNull(sumExpression, nameof(sumExpression));

            _sql.Append("SUM(");

            VisitExpression(sumExpression.Expression);

            _sql.Append(")");

            return sumExpression;
        }

        public virtual Expression VisitMinExpression(MinExpression minExpression)
        {
            Check.NotNull(minExpression, nameof(minExpression));

            _sql.Append("MIN(");

            VisitExpression(minExpression.Expression);

            _sql.Append(")");

            return minExpression;
        }

        public virtual Expression VisitMaxExpression(MaxExpression maxExpression)
        {
            Check.NotNull(maxExpression, nameof(maxExpression));

            _sql.Append("MAX(");

            VisitExpression(maxExpression.Expression);

            _sql.Append(")");

            return maxExpression;
        }

        public virtual Expression VisitInExpression(InExpression inExpression)
        {
            var inValues = ProcessInExpressionValues(inExpression.Values);
            var inValuesNotNull = ExtractNonNullExpressionValues(inValues);

            if (inValues.Count != inValuesNotNull.Count)
            {
                var nullSemanticsInExpression = Expression.OrElse(
                    new InExpression(inExpression.Operand, inValuesNotNull),
                    new IsNullExpression(inExpression.Operand));

                return VisitExpression(nullSemanticsInExpression);
            }

            if (inValuesNotNull.Count > 0)
            {
                VisitExpression(inExpression.Operand);

                _sql.Append(" IN (");

                VisitJoin(inValuesNotNull);

                _sql.Append(")");
            }
            else
            {
                _sql.Append("1 = 0");
            }

            return inExpression;
        }

        protected virtual Expression VisitNotInExpression(InExpression inExpression)
        {
            var inValues = ProcessInExpressionValues(inExpression.Values);
            var inValuesNotNull = ExtractNonNullExpressionValues(inValues);

            if (inValues.Count != inValuesNotNull.Count)
            {
                var nullSemanticsNotInExpression = Expression.AndAlso(
                    Expression.Not(new InExpression(inExpression.Operand, inValuesNotNull)),
                    Expression.Not(new IsNullExpression(inExpression.Operand)));

                return VisitExpression(nullSemanticsNotInExpression);
            }

            if (inValues.Count > 0)
            {
                VisitExpression(inExpression.Operand);

                _sql.Append(" NOT IN (");

                VisitJoin(inValues);

                _sql.Append(")");
            }
            else
            {
                _sql.Append("1 = 1");
            }

            return inExpression;
        }

        protected virtual IReadOnlyList<Expression> ProcessInExpressionValues(
            IReadOnlyList<Expression> inExpressionValues)
        {
            var inConstants = new List<Expression>();
            foreach (var inValue in inExpressionValues)
            {
                var inConstant = inValue as ConstantExpression;
                if (inConstant != null)
                {
                    inConstants.Add(inConstant);
                    continue;
                }

                var inParameter = inValue as ParameterExpression;
                if (inParameter != null)
                {
                    var parameterValue = _parameterValues[inParameter.Name];
                    var valuesCollection = parameterValue as IEnumerable;

                    if (valuesCollection != null
                        && parameterValue.GetType() != typeof(string)
                        && parameterValue.GetType() != typeof(byte[]))
                    {
                        inConstants.AddRange(valuesCollection.Cast<object>().Select(Expression.Constant));
                    }
                    else
                    {
                        inConstants.Add(inParameter);
                    }
                }
            }

            return inConstants;
        }

        protected virtual IReadOnlyList<Expression> ExtractNonNullExpressionValues(
            IReadOnlyList<Expression> inExpressionValues)
        {
            var inValuesNotNull = new List<Expression>();
            foreach (var inValue in inExpressionValues)
            {
                var inConstant = inValue as ConstantExpression;
                if (inConstant?.Value != null)
                {
                    inValuesNotNull.Add(inValue);
                    continue;
                }

                var inParameter = inValue as ParameterExpression;
                if (inParameter != null
                    && _parameterValues[inParameter.Name] != null)
                {
                    inValuesNotNull.Add(inValue);
                }
            }

            return inValuesNotNull;
        }

        public virtual Expression VisitInnerJoinExpression(InnerJoinExpression innerJoinExpression)
        {
            Check.NotNull(innerJoinExpression, nameof(innerJoinExpression));

            _sql.Append("INNER JOIN ");

            VisitExpression(innerJoinExpression.TableExpression);

            _sql.Append(" ON ");

            VisitExpression(innerJoinExpression.Predicate);

            return innerJoinExpression;
        }

        public virtual Expression VisitOuterJoinExpression(LeftOuterJoinExpression leftOuterJoinExpression)
        {
            Check.NotNull(leftOuterJoinExpression, nameof(leftOuterJoinExpression));

            _sql.Append("LEFT JOIN ");

            VisitExpression(leftOuterJoinExpression.TableExpression);

            _sql.Append(" ON ");

            VisitExpression(leftOuterJoinExpression.Predicate);

            return leftOuterJoinExpression;
        }

        protected virtual void GenerateTop([NotNull] SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            if (selectExpression.Limit != null
                && selectExpression.Offset == null)
            {
                _sql.Append("TOP(")
                    .Append(selectExpression.Limit)
                    .Append(") ");
            }
        }

        protected virtual void GenerateLimitOffset([NotNull] SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            if (selectExpression.Offset != null)
            {
                if (!selectExpression.OrderBy.Any())
                {
                    throw new InvalidOperationException(Strings.SkipNeedsOrderBy);
                }

                _sql.Append(" OFFSET ")
                    .Append(selectExpression.Offset)
                    .Append(" ROWS");

                if (selectExpression.Limit != null)
                {
                    _sql.Append(" FETCH NEXT ")
                        .Append(selectExpression.Limit)
                        .Append(" ROWS ONLY");
                }
            }
        }

        public virtual Expression VisitCaseExpression(CaseExpression caseExpression)
        {
            Check.NotNull(caseExpression, nameof(caseExpression));

            var nodeType = caseExpression.When.NodeType;

            if (nodeType == ExpressionType.Conditional)
            {
                var conditionalExpression = (ConditionalExpression)caseExpression.When;

                _sql.AppendLine("CASE");
                using (_sql.Indent())
                {
                    _sql.AppendLine("WHEN");

                    using (_sql.Indent())
                    {
                        _sql.Append("(");
                        VisitExpression(conditionalExpression.Test);
                        _sql.AppendLine(")");
                    }

                    _sql.Append("THEN ");
                    VisitExpression(conditionalExpression.IfTrue);
                    _sql.Append(" ELSE ");
                    VisitExpression(conditionalExpression.IfFalse);
                    _sql.AppendLine();
                }

                _sql.Append("END");
            }
            else
            {
                _sql.AppendLine("CASE");

                using (_sql.Indent())
                {
                    _sql.AppendLine("WHEN");
                    using (_sql.Indent())
                    {
                        _sql.Append("(");
                        VisitExpression(caseExpression.When);
                        if (caseExpression.When.IsSimpleExpression())
                        {
                            _sql.Append(" = ");
                            _sql.Append(TrueLiteral);
                        }

                        _sql.AppendLine(")");
                    }

                    _sql.Append("THEN ");
                    _sql.Append(TypedTrueLiteral);
                    _sql.Append(" ELSE ");
                    _sql.AppendLine(TypedFalseLiteral);
                }

                _sql.Append("END");
            }

            return caseExpression;
        }

        public virtual Expression VisitExistsExpression(ExistsExpression existsExpression)
        {
            Check.NotNull(existsExpression, nameof(existsExpression));

            _sql.AppendLine("EXISTS (");

            using (_sql.Indent())
            {
                VisitExpression(existsExpression.Expression);
            }

            _sql.AppendLine(")");

            return existsExpression;
        }

        protected override Expression VisitBinaryExpression([NotNull] BinaryExpression binaryExpression)
        {
            Check.NotNull(binaryExpression, nameof(binaryExpression));

            if (binaryExpression.NodeType == ExpressionType.Coalesce)
            {
                _sql.Append("COALESCE(");
                VisitExpression(binaryExpression.Left);
                _sql.Append(", ");
                VisitExpression(binaryExpression.Right);
                _sql.Append(")");
            }
            else
            {
                var needParentheses = !binaryExpression.Left.IsSimpleExpression()
                                      || !binaryExpression.Right.IsSimpleExpression()
                                      || binaryExpression.IsLogicalOperation();

                if (needParentheses)
                {
                    _sql.Append("(");
                }

                VisitExpression(binaryExpression.Left);

                if (binaryExpression.IsLogicalOperation()
                    && binaryExpression.Left.IsSimpleExpression())
                {
                    _sql.Append(" = ");
                    _sql.Append(TrueLiteral);
                }

                string op;

                switch (binaryExpression.NodeType)
                {
                    case ExpressionType.Equal:
                        op = " = ";
                        break;
                    case ExpressionType.NotEqual:
                        op = " <> ";
                        break;
                    case ExpressionType.GreaterThan:
                        op = " > ";
                        break;
                    case ExpressionType.GreaterThanOrEqual:
                        op = " >= ";
                        break;
                    case ExpressionType.LessThan:
                        op = " < ";
                        break;
                    case ExpressionType.LessThanOrEqual:
                        op = " <= ";
                        break;
                    case ExpressionType.AndAlso:
                        op = " AND ";
                        break;
                    case ExpressionType.OrElse:
                        op = " OR ";
                        break;
                    case ExpressionType.Add:
                        op = (binaryExpression.Left.Type == typeof(string)
                              && binaryExpression.Right.Type == typeof(string))
                            ? " " + ConcatOperator + " "
                            : " + ";
                        break;
                    case ExpressionType.Subtract:
                        op = " - ";
                        break;
                    case ExpressionType.Multiply:
                        op = " * ";
                        break;
                    case ExpressionType.Divide:
                        op = " / ";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _sql.Append(op);

                VisitExpression(binaryExpression.Right);

                if (binaryExpression.IsLogicalOperation()
                    && binaryExpression.Right.IsSimpleExpression())
                {
                    _sql.Append(" = ");
                    _sql.Append(TrueLiteral);
                }

                if (needParentheses)
                {
                    _sql.Append(")");
                }
            }

            return binaryExpression;
        }

        public virtual Expression VisitColumnExpression(ColumnExpression columnExpression)
        {
            Check.NotNull(columnExpression, nameof(columnExpression));

            _sql.Append(DelimitIdentifier(columnExpression.TableAlias))
                .Append(".")
                .Append(DelimitIdentifier(columnExpression.Name));

            return columnExpression;
        }

        public virtual Expression VisitAliasExpression(AliasExpression aliasExpression)
        {
            Check.NotNull(aliasExpression, nameof(aliasExpression));
            if (!aliasExpression.Projected)
            {
                VisitExpression(aliasExpression.Expression);
                if (aliasExpression.Alias != null)
                {
                    _sql.Append(" AS ");
                }
            }
            if (aliasExpression.Alias != null)
            {
                _sql.Append(DelimitIdentifier(aliasExpression.Alias));
            }

            return aliasExpression;
        }

        public virtual Expression VisitIsNullExpression(IsNullExpression isNullExpression)
        {
            Check.NotNull(isNullExpression, nameof(isNullExpression));

            VisitExpression(isNullExpression.Operand);

            _sql.Append(" IS NULL");

            return isNullExpression;
        }

        public virtual Expression VisitIsNotNullExpression([NotNull] IsNullExpression isNotNullExpression)
        {
            Check.NotNull(isNotNullExpression, nameof(isNotNullExpression));

            VisitExpression(isNotNullExpression.Operand);

            _sql.Append(" IS NOT NULL");

            return isNotNullExpression;
        }

        public virtual Expression VisitLikeExpression(LikeExpression likeExpression)
        {
            Check.NotNull(likeExpression, nameof(likeExpression));

            VisitExpression(likeExpression.Match);

            _sql.Append(" LIKE ");

            VisitExpression(likeExpression.Pattern);

            return likeExpression;
        }

        public virtual Expression VisitLiteralExpression(LiteralExpression literalExpression)
        {
            Check.NotNull(literalExpression, nameof(literalExpression));

            _sql.Append(GenerateLiteral(literalExpression.Literal));

            return literalExpression;
        }

        protected override Expression VisitUnaryExpression(UnaryExpression unaryExpression)
        {
            Check.NotNull(unaryExpression, nameof(unaryExpression));

            if (unaryExpression.NodeType == ExpressionType.Not)
            {
                var inExpression = unaryExpression.Operand as InExpression;
                if (inExpression != null)
                {
                    return VisitNotInExpression(inExpression);
                }

                var isNullExpression = unaryExpression.Operand as IsNullExpression;
                if (isNullExpression != null)
                {
                    return VisitIsNotNullExpression(isNullExpression);
                }

                var isColumnOrParameterOperand =
                    unaryExpression.Operand is ColumnExpression
                    || unaryExpression.Operand is ParameterExpression
                    || unaryExpression.Operand.IsAliasWithColumnExpression();

                if (!isColumnOrParameterOperand)
                {
                    _sql.Append("NOT (");
                    VisitExpression(unaryExpression.Operand);
                    _sql.Append(")");
                }
                else
                {
                    VisitExpression(unaryExpression.Operand);
                    _sql.Append(" = ");
                    _sql.Append(FalseLiteral);
                }

                return unaryExpression;
            }

            if (unaryExpression.NodeType == ExpressionType.Convert)
            {
                VisitExpression(unaryExpression.Operand);

                return unaryExpression;
            }

            return base.VisitUnaryExpression(unaryExpression);
        }

        protected override Expression VisitConstantExpression(ConstantExpression constantExpression)
        {
            Check.NotNull(constantExpression, nameof(constantExpression));

            _sql.Append(constantExpression.Value == null
                ? "NULL"
                : GenerateLiteral((dynamic)constantExpression.Value));

            return constantExpression;
        }

        protected override Expression VisitParameterExpression(ParameterExpression parameterExpression)
        {
            _sql.Append(ParameterPrefix + parameterExpression.Name);

            if (_commandParameters.All(commandParameter => commandParameter.Name != parameterExpression.Name))
            {
                _commandParameters.Add(new CommandParameter(parameterExpression.Name, _parameterValues[parameterExpression.Name]));
            }

            return parameterExpression;
        }

        protected override Exception CreateUnhandledItemException<T>(T unhandledItem, string visitMethod)
            => new NotImplementedException(visitMethod);

        // TODO: Share the code below (#1559)

        private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffK";
        private const string DateTimeOffsetFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

        protected virtual string GenerateLiteral([NotNull] object value)
            => string.Format(CultureInfo.InvariantCulture, "{0}", value);

        protected virtual string GenerateLiteral(bool value)
            => value ? TrueLiteral : FalseLiteral;

        protected virtual string GenerateLiteral([NotNull] string value)
            => "'" + EscapeLiteral(Check.NotNull(value, nameof(value))) + "'";

        protected virtual string GenerateLiteral(Guid value)
            => "'" + value + "'";

        protected virtual string GenerateLiteral(DateTime value)
            => "'" + value.ToString(DateTimeFormat, CultureInfo.InvariantCulture) + "'";

        protected virtual string GenerateLiteral(DateTimeOffset value)
            => "'" + value.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture) + "'";

        protected virtual string GenerateLiteral(TimeSpan value)
            => "'" + value + "'";

        protected virtual string GenerateLiteral([NotNull] byte[] value)
        {
            var stringBuilder = new StringBuilder("0x");

            foreach (var @byte in value)
            {
                stringBuilder.Append(@byte.ToString("X2", CultureInfo.InvariantCulture));
            }

            return stringBuilder.ToString();
        }

        protected virtual string EscapeLiteral([NotNull] string literal)
            => Check.NotNull(literal, nameof(literal)).Replace("'", "''");

        protected virtual string DelimitIdentifier([NotNull] string identifier)
            => "\"" + Check.NotEmpty(identifier, nameof(identifier)) + "\"";

        private class NullComparisonTransformingVisitor : ExpressionTreeVisitor
        {
            private readonly IDictionary<string, object> _parameterValues;

            public NullComparisonTransformingVisitor(IDictionary<string, object> parameterValues)
            {
                _parameterValues = parameterValues;
            }

            protected override Expression VisitBinaryExpression(BinaryExpression expression)
            {
                if (expression.NodeType == ExpressionType.Equal
                    || expression.NodeType == ExpressionType.NotEqual)
                {
                    var parameter
                        = expression.Right as ParameterExpression
                          ?? expression.Left as ParameterExpression;

                    object parameterValue;
                    if (parameter != null
                        && _parameterValues.TryGetValue(parameter.Name, out parameterValue)
                        && parameterValue == null)
                    {
                        var columnExpression
                            = expression.Left.GetColumnExpression()
                              ?? expression.Right.GetColumnExpression();

                        if (columnExpression != null)
                        {
                            return
                                expression.NodeType == ExpressionType.Equal
                                    ? (Expression)new IsNullExpression(columnExpression)
                                    : Expression.Not(new IsNullExpression(columnExpression));
                        }
                    }
                }

                return base.VisitBinaryExpression(expression);
            }
        }

        private class ReducingExpressionVisitor : ExpressionTreeVisitor
        {
            public override Expression VisitExpression(Expression node)
            {
                if (node != null && node.CanReduce)
                {
                    var reduced = node.Reduce();
                    return base.VisitExpression(reduced);
                }

                return base.VisitExpression(node);
            }
        }
    }
}
