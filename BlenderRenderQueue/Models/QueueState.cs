namespace BlenderRenderQueue.Models;

/// <summary>
/// 队列状态枚举
/// </summary>
public enum QueueState
{
    /// <summary>
    /// 空闲状态 - 没有任务在运行，队列已停止
    /// </summary>
    Idle,
    
    /// <summary>
    /// 运行中 - 有任务正在运行
    /// </summary>
    Running,
    
    /// <summary>
    /// 暂停 - 队列被暂停，但任务可能仍在运行
    /// </summary>
    Paused,
    
    /// <summary>
    /// 完成 - 所有任务已完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 错误 - 队列遇到错误
    /// </summary>
    Error
}
