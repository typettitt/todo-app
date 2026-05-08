import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { getMe, login, logout, register } from './api';
import { authKeys } from './queryKeys';
import type { AuthUser } from './schemas';
import { clearPrivateData } from './useAuthBoundary';

export function useMe() {
  return useQuery({
    queryKey: authKeys.me(),
    queryFn: getMe,
    retry: false,
    staleTime: 60_000,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: login,
    onSuccess: async (user: AuthUser) => {
      // Drop the previous owner's private cache before seeding the new user.
      // Otherwise an Alice→Bob switch in the same SPA session repaints with
      // Alice's todos before Bob's first refetch lands.
      await clearPrivateData(queryClient);
      queryClient.setQueryData(authKeys.me(), user);
    },
  });
}

export function useRegister() {
  const queryClient = useQueryClient();

  // Register returns the same acknowledgement for new-email and duplicate-email
  // branches, so we cannot optimistically seed `authKeys.me()`. Invalidate and
  // let `/me` reveal whether a session cookie was issued.
  return useMutation({
    mutationFn: register,
    onSuccess: async () => {
      await clearPrivateData(queryClient);
      await queryClient.invalidateQueries({ queryKey: authKeys.me() });
    },
  });
}

export function useLogout() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: logout,
    onSettled: () => {
      queryClient.clear();
      navigate('/login?returnTo=/', { replace: true });
    },
  });
}
