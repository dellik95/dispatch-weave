using FluentResults;

namespace DispatchWeave.Results;

public interface ICommand : IRequest<Result>;
public interface ICommand<T> : IRequest<Result<T>>;
public interface IQuery<T> : IRequest<Result<T>>;
public interface ICommandHandler<in T> : IRequestHandler<T, Result> where T : ICommand;
public interface ICommandHandler<in T, TResult> : IRequestHandler<T, Result<TResult>> where T : ICommand<TResult>;
public interface IQueryHandler<in T, TResult> : IRequestHandler<T, Result<TResult>> where T : IQuery<TResult>;
