namespace BlenderRenderQueue.Models;

public enum RenderTaskStatus
{
    Pending, // 等待中
    Running, // 运行中
    Paused, // 已暂停
    Completed, // 已完成
    Failed, // 失败
    Cancelled // 已取消
}