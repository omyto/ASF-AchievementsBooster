namespace AchievementsBooster.Base;
internal readonly struct TaskResult(bool success, string message = "") {
  internal readonly bool Success = success;
  internal readonly string Message = message;
}
