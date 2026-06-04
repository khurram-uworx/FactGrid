using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace FactGrid.AspNet.Services;

public class QueryValidationService
{
    static readonly GenericDialect Dialect = new();

    static ValidationResult Pass() => new(true, null);
    static ValidationResult Fail(string error) => new(false, error);

    readonly SqlQueryParser parser = new();

    public ValidationResult Validate(string query)
    {
        try
        {
            var statements = parser.Parse(query.AsSpan(), Dialect);

            if (statements.Count != 1)
                return Fail("Only single-statement queries are supported");

            if (statements[0] is not Statement.Select)
                return Fail("Only SELECT statements are supported");

            return Pass();
        }
        catch (Exception ex)
        {
            return Fail($"Parse error: {ex.Message}");
        }
    }

    public ValidationResult ValidateScoped(string query, string allowedTableName)
        => ValidateTables(query, [allowedTableName]);

    public ValidationResult ValidateTables(string query, IEnumerable<string> allowedTableNames)
    {
        var allowed = new HashSet<string>(allowedTableNames, StringComparer.OrdinalIgnoreCase);

        try
        {
            var statements = parser.Parse(query.AsSpan(), Dialect);

            if (statements.Count != 1)
                return Fail("Only single-statement queries are supported");

            if (statements[0] is not Statement.Select selectStmt)
                return Fail("Only SELECT statements are supported");

            var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectTableReferences(selectStmt.Query, tables, cteNames);

            var disallowed = tables
                .Where(t => !cteNames.Contains(t) && !allowed.Contains(t))
                .Distinct()
                .ToList();

            if (disallowed.Count > 0)
            {
                var list = string.Join(", ", disallowed);
                var allowedList = string.Join(", ", allowed);
                return Fail($"Query references non-registered tables: {list}. Allowed tables: {allowedList}");
            }

            return Pass();
        }
        catch (Exception ex)
        {
            return Fail($"Parse error: {ex.Message}");
        }
    }

    void CollectTableReferences(Query query, HashSet<string> tables, HashSet<string> cteNames)
    {
        if (query.With is { } with)
        {
            foreach (var cte in with.CteTables)
                cteNames.Add(cte.Alias.Name.Value);

            foreach (var cte in with.CteTables)
                CollectTableReferences(cte.Query, tables, cteNames);
        }

        WalkSetExpression(query.Body, tables, cteNames);

        if (query.OrderBy is { } orderBy)
            WalkOrderBy(orderBy, tables, cteNames);

        if (query.Limit is { } limit)
            WalkExpression(limit, tables, cteNames);

        if (query.Offset is { } offset)
            WalkExpression(offset.Value, tables, cteNames);

        if (query.Fetch?.Quantity is { } quantity)
            WalkExpression(quantity, tables, cteNames);

        if (query.LimitBy is { } limitBy)
        {
            foreach (var expression in limitBy)
                WalkExpression(expression, tables, cteNames);
        }
    }

    void WalkSetExpression(SetExpression expr, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (expr)
        {
            case SetExpression.SelectExpression se:
                WalkSelect(se.Select, tables, cteNames);
                break;
            case SetExpression.SetOperation so:
                WalkSetExpression(so.Left, tables, cteNames);
                WalkSetExpression(so.Right, tables, cteNames);
                break;
            case SetExpression.QueryExpression qe:
                CollectTableReferences(qe.Query, tables, cteNames);
                break;
        }
    }

    void WalkSelect(Select select, HashSet<string> tables, HashSet<string> cteNames)
    {
        if (select.From is { } from)
        {
            foreach (var twj in from)
            {
                if (twj.Relation is { } rel)
                    WalkTableFactor(rel, tables, cteNames);

                if (twj.Joins is { } joins)
                {
                    foreach (var join in joins)
                    {
                        if (join.Relation is { } jrel)
                            WalkTableFactor(jrel, tables, cteNames);

                        WalkJoinOperator(join.JoinOperator, tables, cteNames);
                    }
                }
            }
        }

        if (select.Selection is { } where)
            WalkExpression(where, tables, cteNames);

        if (select.Having is { } having)
            WalkExpression(having, tables, cteNames);

        foreach (var item in select.Projection)
            WalkSelectItem(item, tables, cteNames);

        if (select.GroupBy is { } groupBy)
            WalkGroupBy(groupBy, tables, cteNames);

        if (select.PreWhere is { } preWhere)
            WalkExpression(preWhere, tables, cteNames);
    }

    void WalkTableFactor(TableFactor factor, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (factor)
        {
            case TableFactor.Table tbl:
                var name = tbl.Name.Values.Last().Value;
                tables.Add(name);
                break;

            case TableFactor.Derived derived:
                CollectTableReferences(derived.SubQuery, tables, cteNames);
                break;

            case TableFactor.NestedJoin nested:
                if (nested.TableWithJoins is { } twj)
                {
                    if (twj.Relation is { } rel2)
                        WalkTableFactor(rel2, tables, cteNames);
                    if (twj.Joins is { } joins)
                    {
                        foreach (var join in joins)
                        {
                            if (join.Relation is { } rel)
                                WalkTableFactor(rel, tables, cteNames);
                            WalkJoinOperator(join.JoinOperator, tables, cteNames);
                        }
                    }
                }
                break;

            case TableFactor.MatchRecognize match:
                WalkTableFactor(match.MatchTable, tables, cteNames);
                foreach (var p in match.PartitionBy)
                    WalkExpression(p, tables, cteNames);
                foreach (var o in match.OrderBy)
                    WalkOrderByExpression(o, tables, cteNames);
                break;

            case TableFactor.Pivot pivot:
                WalkTableFactor(pivot.TableFactor, tables, cteNames);
                break;

            case TableFactor.Unpivot unpivot:
                WalkTableFactor(unpivot.TableFactor, tables, cteNames);
                break;
        }
    }

    void WalkOrderBy(OrderBy orderBy, HashSet<string> tables, HashSet<string> cteNames)
    {
        if (orderBy.Expressions is { } expressions)
        {
            foreach (var expression in expressions)
                WalkOrderByExpression(expression, tables, cteNames);
        }

        if (orderBy.Interpolate?.Expressions is { } interpolateExpressions)
        {
            foreach (var interpolate in interpolateExpressions)
            {
                if (interpolate.Expression is { } expression)
                    WalkExpression(expression, tables, cteNames);
            }
        }
    }

    void WalkOrderByExpression(OrderByExpression orderBy, HashSet<string> tables, HashSet<string> cteNames)
    {
        WalkExpression(orderBy.Expression, tables, cteNames);

        if (orderBy.WithFill is not { } withFill)
            return;

        if (withFill.From is { } from)
            WalkExpression(from, tables, cteNames);
        if (withFill.To is { } to)
            WalkExpression(to, tables, cteNames);
        if (withFill.Step is { } step)
            WalkExpression(step, tables, cteNames);
    }

    void WalkJoinOperator(JoinOperator? op, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (op)
        {
            case JoinOperator.ConstrainedJoinOperator constrained:
                WalkJoinConstraint(constrained.JoinConstraint, tables, cteNames);
                break;
            case JoinOperator.AsOf asof:
                WalkExpression(asof.MatchCondition, tables, cteNames);
                WalkJoinConstraint(asof.Constraint, tables, cteNames);
                break;
        }
    }

    void WalkJoinConstraint(JoinConstraint constraint, HashSet<string> tables, HashSet<string> cteNames)
    {
        if (constraint is JoinConstraint.On on)
            WalkExpression(on.Expression, tables, cteNames);
    }

    void WalkSelectItem(SelectItem item, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (item)
        {
            case SelectItem.UnnamedExpression ue:
                WalkExpression(ue.Expression, tables, cteNames);
                break;
            case SelectItem.ExpressionWithAlias ewa:
                WalkExpression(ewa.Expression, tables, cteNames);
                break;
        }
    }

    void WalkGroupBy(GroupByExpression groupBy, HashSet<string> tables, HashSet<string> cteNames)
    {
        if (groupBy is GroupByExpression.Expressions exprs)
        {
            foreach (var e in exprs.ColumnNames)
                WalkExpression(e, tables, cteNames);
        }
    }

    void WalkExpression(Expression expr, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (expr)
        {
            case Expression.Subquery sub:
                CollectTableReferences(sub.Query, tables, cteNames);
                break;

            case Expression.Exists exists:
                CollectTableReferences(exists.SubQuery, tables, cteNames);
                break;

            case Expression.InSubquery inSub:
                CollectTableReferences(inSub.SubQuery, tables, cteNames);
                break;

            case Expression.BinaryOp binary:
                WalkExpression(binary.Left, tables, cteNames);
                WalkExpression(binary.Right, tables, cteNames);
                break;

            case Expression.UnaryOp unary:
                WalkExpression(unary.Expression, tables, cteNames);
                break;

            case Expression.Nested nested:
                WalkExpression(nested.Expression, tables, cteNames);
                break;

            case Expression.Case caseExpr:
                if (caseExpr.Operand is { } operand)
                    WalkExpression(operand, tables, cteNames);
                foreach (var c in caseExpr.Conditions)
                    WalkExpression(c, tables, cteNames);
                foreach (var r in caseExpr.Results)
                    WalkExpression(r, tables, cteNames);
                if (caseExpr.ElseResult is { } elseResult)
                    WalkExpression(elseResult, tables, cteNames);
                break;

            case Expression.InList inList:
                WalkExpression(inList.Expression, tables, cteNames);
                foreach (var e in inList.List)
                    WalkExpression(e, tables, cteNames);
                break;

            case Expression.Between between:
                WalkExpression(between.Expression, tables, cteNames);
                WalkExpression(between.Low, tables, cteNames);
                WalkExpression(between.High, tables, cteNames);
                break;

            case Expression.Like like:
                if (like.Expression is { } le)
                    WalkExpression(le, tables, cteNames);
                WalkExpression(like.Pattern, tables, cteNames);
                break;

            case Expression.ILike iLike:
                WalkExpression(iLike.Expression, tables, cteNames);
                WalkExpression(iLike.Pattern, tables, cteNames);
                break;

            case Expression.SimilarTo similarTo:
                WalkExpression(similarTo.Expression, tables, cteNames);
                WalkExpression(similarTo.Pattern, tables, cteNames);
                break;

            case Expression.RLike rLike:
                WalkExpression(rLike.Expression, tables, cteNames);
                WalkExpression(rLike.Pattern, tables, cteNames);
                break;

            case Expression.AllOp all:
                WalkExpression(all.Left, tables, cteNames);
                WalkExpression(all.Right, tables, cteNames);
                break;

            case Expression.AnyOp any:
                WalkExpression(any.Left, tables, cteNames);
                WalkExpression(any.Right, tables, cteNames);
                break;

            case Expression.IsDistinctFrom idf:
                WalkExpression(idf.Expression1, tables, cteNames);
                WalkExpression(idf.Expression2, tables, cteNames);
                break;

            case Expression.IsNotDistinctFrom indf:
                WalkExpression(indf.Expression1, tables, cteNames);
                WalkExpression(indf.Expression2, tables, cteNames);
                break;

            case Expression.Cast cast:
                WalkExpression(cast.Expression, tables, cteNames);
                break;

            case Expression.Extract extract:
                WalkExpression(extract.Expression, tables, cteNames);
                break;

            case Expression.Position pos:
                WalkExpression(pos.Expression, tables, cteNames);
                WalkExpression(pos.In, tables, cteNames);
                break;

            case Expression.Substring ss:
                WalkExpression(ss.Expression, tables, cteNames);
                if (ss.SubstringFrom is { } sf)
                    WalkExpression(sf, tables, cteNames);
                if (ss.SubstringFor is { } sfor)
                    WalkExpression(sfor, tables, cteNames);
                break;

            case Expression.Trim trim:
                WalkExpression(trim.Expression, tables, cteNames);
                if (trim.TrimWhat is { } tw)
                    WalkExpression(tw, tables, cteNames);
                if (trim.TrimCharacters is { } tchars)
                    foreach (var tc in tchars)
                        WalkExpression(tc, tables, cteNames);
                break;

            case Expression.Overlay overlay:
                WalkExpression(overlay.Expression, tables, cteNames);
                WalkExpression(overlay.OverlayWhat, tables, cteNames);
                WalkExpression(overlay.OverlayFrom, tables, cteNames);
                if (overlay.OverlayFor is { } ovf)
                    WalkExpression(ovf, tables, cteNames);
                break;

            case Expression.Floor floor:
                WalkExpression(floor.Expression, tables, cteNames);
                break;

            case Expression.Ceil ceil:
                WalkExpression(ceil.Expression, tables, cteNames);
                break;

            case Expression.AtTimeZone atTz:
                WalkExpression(atTz.Timestamp, tables, cteNames);
                WalkExpression(atTz.TimeZone, tables, cteNames);
                break;

            case Expression.Interval interval:
                WalkExpression(interval.Value, tables, cteNames);
                break;

            case Expression.Convert convert:
                WalkExpression(convert.Expression, tables, cteNames);
                foreach (var s in convert.Styles)
                    WalkExpression(s, tables, cteNames);
                break;

            case Expression.OuterJoin outerJoin:
                WalkExpression(outerJoin.Expression, tables, cteNames);
                break;

            case Expression.Prior prior:
                WalkExpression(prior.Expression, tables, cteNames);
                break;

            case Expression.IsNull isn:
                WalkExpression(isn.Expression, tables, cteNames);
                break;

            case Expression.IsNotNull isnn:
                WalkExpression(isnn.Expression, tables, cteNames);
                break;

            case Expression.IsTrue isT:
                WalkExpression(isT.Expression, tables, cteNames);
                break;

            case Expression.IsNotTrue isNT:
                WalkExpression(isNT.Expression, tables, cteNames);
                break;

            case Expression.IsFalse isF:
                WalkExpression(isF.Expression, tables, cteNames);
                break;

            case Expression.IsNotFalse isNF:
                WalkExpression(isNF.Expression, tables, cteNames);
                break;

            case Expression.IsUnknown isU:
                WalkExpression(isU.Expression, tables, cteNames);
                break;

            case Expression.IsNotUnknown isNU:
                WalkExpression(isNU.Expression, tables, cteNames);
                break;

            case Expression.Subscript subscript:
                WalkExpression(subscript.Expression, tables, cteNames);
                break;

            case Expression.JsonAccess jsonAccess:
                WalkExpression(jsonAccess.Value, tables, cteNames);
                break;

            case Expression.CompositeAccess compAccess:
                WalkExpression(compAccess.Expression, tables, cteNames);
                break;

            case Expression.Tuple tuple:
                foreach (var e in tuple.Expressions)
                    WalkExpression(e, tables, cteNames);
                break;

            case Expression.Named named:
                WalkExpression(named.Expression, tables, cteNames);
                break;

            case Expression.Struct str:
                foreach (var v in str.Values)
                    WalkExpression(v, tables, cteNames);
                break;

            case Expression.Function func:
                WalkFunctionArguments(func.Args, tables, cteNames);
                if (func.Filter is { } filter)
                    WalkExpression(filter, tables, cteNames);
                if (func.WithinGroup is { } withinGroup)
                    foreach (var o in withinGroup)
                        WalkOrderByExpression(o, tables, cteNames);
                break;

            case Expression.MapAccess mapAccess:
                foreach (var k in mapAccess.Keys)
                    WalkExpression(k.Key, tables, cteNames);
                break;

            case Expression.Collate collate:
                WalkExpression(collate.Expression, tables, cteNames);
                break;
        }
    }

    void WalkFunctionArguments(FunctionArguments args, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (args)
        {
            case FunctionArguments.Subquery s:
                CollectTableReferences(s.Query, tables, cteNames);
                break;
            case FunctionArguments.List list:
                if (list.ArgumentList.Args is { } funcArgs)
                {
                    foreach (var fa in funcArgs)
                        WalkFunctionArg(fa, tables, cteNames);
                }
                break;
        }
    }

    void WalkFunctionArg(FunctionArg arg, HashSet<string> tables, HashSet<string> cteNames)
    {
        switch (arg)
        {
            case FunctionArg.Named named:
                WalkFunctionArgExpression(named.Arg, tables, cteNames);
                break;
            case FunctionArg.Unnamed unnamed:
                WalkFunctionArgExpression(unnamed.FunctionArgExpression, tables, cteNames);
                break;
        }
    }

    void WalkFunctionArgExpression(FunctionArgExpression expr, HashSet<string> tables, HashSet<string> cteNames)
    {
        if (expr is FunctionArgExpression.FunctionExpression fe)
            WalkExpression(fe.Expression, tables, cteNames);
    }

    public record ValidationResult(bool IsValid, string? Error)
    {
        public void Deconstruct(out bool IsValid, out string? Error)
        {
            IsValid = this.IsValid;
            Error = this.Error;
        }
    }
}
