import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { attendanceApi } from '../api/attendanceApi';
import { useToastStore } from '../store/attendanceStore';
import { getErrorMessage } from '../utils/errorUtils';
import { formatZurichTime } from '../utils/dateUtils';

export function useAttendance() {
  const queryClient = useQueryClient();
  const showToast = useToastStore((s) => s.showToast);

  const statusQuery = useQuery({
    queryKey: ['attendance', 'status'],
    queryFn: () => attendanceApi.getStatus().then((r) => r.data),
    refetchInterval: 30_000, // refresh every 30 seconds (display)
    staleTime: 10_000,
  });

  const clockInMutation = useMutation({
    mutationFn: (notes?: string) => attendanceApi.clockIn(notes).then((r) => r.data),
    onSuccess: (data) => {
      showToast('success', `Clocked in at ${formatZurichTime(data.clockInUtc)} (Zurich time)`);
      queryClient.invalidateQueries({ queryKey: ['attendance'] });
    },
    onError: (error) => showToast('error', getErrorMessage(error)),
  });

  const clockOutMutation = useMutation({
    mutationFn: () => attendanceApi.clockOut().then((r) => r.data),
    onSuccess: (data) => {
      showToast('success', `Clocked out. Duration: ${data.durationFormatted}`);
      queryClient.invalidateQueries({ queryKey: ['attendance'] });
    },
    onError: (error) => showToast('error', getErrorMessage(error)),
  });

  return {
    status: statusQuery.data,
    isLoading: statusQuery.isLoading,
    clockIn: clockInMutation.mutateAsync,
    clockOut: clockOutMutation.mutateAsync,
    isClockingIn: clockInMutation.isPending,
    isClockingOut: clockOutMutation.isPending,
    clockInError: clockInMutation.error,
    clockOutError: clockOutMutation.error,
  };
}
